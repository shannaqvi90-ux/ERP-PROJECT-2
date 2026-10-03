using System.Reflection;
using System.Runtime.CompilerServices;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, process-wide state. Every tenant shares one process, so anything that outlives a request
/// (a static field, a field of a singleton service, an in-memory, distributed or output cache)
/// can carry one tenant's data to another even when every query is tenant-bound. The HTTP attack
/// (<see cref="TenantActivity"/>) catches such a leak when it happens; this check catches the
/// state itself: every piece of process-wide state in the product's own assemblies must be
/// immutable or reviewed in tests/Gates/process-state-allowlist.txt with the reason it cannot
/// hold tenant data. Stale entries fail too, so the list stays the true inventory. Variables
/// captured by endpoint lambdas count as process-wide state too (<see cref="EndpointClosures"/>).
/// </summary>
public sealed class G1ProcessStateTests(GateFixture fixture)
{
    private const string AllowlistPath = "tests/Gates/process-state-allowlist.txt";

    [Fact]
    public void Process_wide_state_is_immutable_or_reviewed()
    {
        var inventory = ProcessState.Inspect(fixture.Env.Factory);
        var reviewed = Repo.ReadReviewedList(AllowlistPath);
        var problems = new List<string>();
        problems.AddRange(reviewed.Where(e => e.Reason.Length == 0).Select(e => $"{e.Entry}: the reviewed entry needs a reason after '#'"));
        var keys = reviewed.Select(e => e.Entry).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(inventory.Findings.Where(f => !keys.Contains(f.Key))
            .Select(f => $"{f.Key}: {f.Why}. Make it immutable, scope it to the request, key it by tenant, or review it in {AllowlistPath}."));
        var found = inventory.Findings.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(keys.Where(k => !found.Contains(k)).Select(k => $"{k}: reviewed in {AllowlistPath} but no longer exists; remove the entry."));

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{inventory.TypesInspected} types, {inventory.FieldsInspected} fields, {inventory.SingletonsInspected} singleton services inspected, " +
            $"{inventory.EndpointsWalked} endpoint delegates walked ({inventory.DelegateObjectsWalked} objects) to {inventory.ClosuresInspected} closures; {inventory.Findings.Count} reviewed findings");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(inventory.FieldsInspected >= Ratchet.Min("g1.processStateFieldsInspected"),
            $"g1.processStateFieldsInspected: {inventory.FieldsInspected}; ratchet minimum {Ratchet.Min("g1.processStateFieldsInspected")}");
        Assert.True(inventory.SingletonsInspected >= Ratchet.Min("g1.singletonsInspected"),
            $"g1.singletonsInspected: {inventory.SingletonsInspected}; ratchet minimum {Ratchet.Min("g1.singletonsInspected")}");
        Assert.True(inventory.EndpointsWalked >= Ratchet.Min("g1.endpointDelegatesWalked"),
            $"g1.endpointDelegatesWalked: {inventory.EndpointsWalked}; ratchet minimum {Ratchet.Min("g1.endpointDelegatesWalked")}");
        Assert.True(inventory.ClosuresInspected >= Ratchet.Min("g1.endpointClosuresInspected"),
            $"g1.endpointClosuresInspected: {inventory.ClosuresInspected}; ratchet minimum {Ratchet.Min("g1.endpointClosuresInspected")}");
        Assert.True(inventory.DelegateObjectsWalked >= Ratchet.Min("g1.endpointDelegateObjectsWalked"),
            $"g1.endpointDelegateObjectsWalked: {inventory.DelegateObjectsWalked}; ratchet minimum {Ratchet.Min("g1.endpointDelegateObjectsWalked")}");
    }
}

/// <summary>One piece of process-wide state and why it is flagged.</summary>
public sealed record ProcessStateFinding(string Key, string Why);

public sealed record ProcessStateInventory(IReadOnlyList<ProcessStateFinding> Findings, int TypesInspected, int FieldsInspected, int SingletonsInspected)
{
    /// <summary>Endpoints whose request delegate was walked for captured state.</summary>
    public int EndpointsWalked { get; init; }

    /// <summary>Closures (captured variables) of the product reached from endpoint delegates.</summary>
    public int ClosuresInspected { get; init; }

    /// <summary>Objects visited while walking endpoint delegates (a walk that reaches nothing is blind).</summary>
    public int DelegateObjectsWalked { get; init; }
}

/// <summary>Finds process-wide state by reflection over the product's assemblies and the app's
/// service registrations.</summary>
public static class ProcessState
{
    /// <summary>Services that keep data across requests by design; registering one needs review.</summary>
    private static readonly string[] CacheServices =
    [
        "Microsoft.Extensions.Caching.Memory.IMemoryCache",
        "Microsoft.Extensions.Caching.Distributed.IDistributedCache",
        "Microsoft.Extensions.Caching.Hybrid.HybridCache",
        "Microsoft.AspNetCore.OutputCaching.IOutputCacheStore",
        "Microsoft.AspNetCore.ResponseCaching.IResponseCachingPolicyProvider",
    ];

    public static ProcessStateInventory Inspect(ErpAppFactory factory)
    {
        // The host's assemblies and those of every module the running app loaded.
        var modules = factory.Services.GetRequiredService<Erp.Kernel.Modules.ModuleCatalog>().Modules.Select(m => m.Assembly);
        var assemblies = ProductAssemblies().Union(modules).Distinct().ToList();
        var descriptors = factory.ServiceDescriptors;
        var serviceTypes = descriptors.Select(d => d.ServiceType).ToHashSet();
        var singletons = new List<Type>();
        foreach (var descriptor in descriptors.Where(d => d.Lifetime == ServiceLifetime.Singleton && !d.IsKeyedService))
        {
            var type = descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
            if (type is null && descriptor.ImplementationFactory is not null && IsProduct(descriptor.ServiceType, assemblies) && !descriptor.ServiceType.ContainsGenericParameters)
            {
                type = factory.Services.GetService(descriptor.ServiceType)?.GetType();
            }
            if (type is not null && IsProduct(type, assemblies))
            {
                singletons.Add(type);
            }
        }
        var inventory = InspectTypes(assemblies.SelectMany(LoadableTypes), singletons.Distinct(), serviceTypes);
        var findings = inventory.Findings.ToList();
        // Variables captured by endpoint lambdas live as long as the endpoint: walk every endpoint's
        // delegate to the product closures and objects it holds.
        var closures = EndpointClosures.Inspect(factory.Services, assemblies, serviceTypes);
        findings.AddRange(closures.Findings);
        inventory = inventory with { EndpointsWalked = closures.Endpoints, ClosuresInspected = closures.Closures, DelegateObjectsWalked = closures.ObjectsWalked };
        foreach (var name in CacheServices)
        {
            if (descriptors.Any(d => d.ServiceType.FullName == name))
            {
                findings.Add(new ProcessStateFinding($"service {name}", "a cache service is registered; anything it stores is shared by every tenant"));
            }
        }
        return inventory with { Findings = findings.OrderBy(f => f.Key, StringComparer.Ordinal).ToList() };
    }

    /// <summary>Static fields of every type, and instance fields of the singleton types, that can
    /// change after start-up or hold a mutable object.</summary>
    public static ProcessStateInventory InspectTypes(IEnumerable<Type> types, IEnumerable<Type> singletonTypes, IReadOnlySet<Type> serviceTypes)
    {
        var findings = new List<ProcessStateFinding>();
        var typeCount = 0;
        var fieldCount = 0;
        foreach (var type in types)
        {
            if (IsGenerated(type))
            {
                continue;
            }
            typeCount++;
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsLiteral)
                {
                    continue;
                }
                fieldCount++;
                if (Problem(field, serviceTypes) is { } why)
                {
                    findings.Add(new ProcessStateFinding($"static {Name(type)}.{FieldName(field)}", why));
                }
            }
        }
        var singletonCount = 0;
        foreach (var type in singletonTypes)
        {
            singletonCount++;
            for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            {
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    fieldCount++;
                    if (Problem(field, serviceTypes) is { } why)
                    {
                        findings.Add(new ProcessStateFinding($"singleton {Name(type)}.{FieldName(field)}", why));
                    }
                }
            }
        }
        return new ProcessStateInventory(findings.DistinctBy(f => f.Key).ToList(), typeCount, fieldCount, singletonCount);
    }

    internal static string? Problem(FieldInfo field, IReadOnlySet<Type> serviceTypes)
    {
        var type = field.FieldType;
        var isService = serviceTypes.Contains(type) || type == typeof(IServiceProvider) ||
                        (type.IsGenericType && serviceTypes.Contains(type.GetGenericTypeDefinition()));
        // A primary-constructor parameter the class keeps (compiler field "<name>P") holding an
        // injected service is a dependency, not state; the service is inspected on its own.
        var capturedParameter = field.Name.StartsWith('<') && field.Name.EndsWith(">P", StringComparison.Ordinal);
        if (!field.IsInitOnly && !(capturedParameter && (isService || IsImmutable(type))))
        {
            return $"a field that can be reassigned at any time ({Describe(type)})";
        }
        if (isService || IsImmutable(type))
        {
            return null;
        }
        return $"a read-only field that holds a mutable {Describe(type)}";
    }

    /// <summary>True when instances of the type cannot change (see <see cref="IsImmutable"/>).</summary>
    public static bool IsImmutableType(Type type) => IsImmutable(type);

    private static readonly Dictionary<Type, bool> Immutability = [];
    private static readonly Lock ImmutabilityLock = new();

    private static readonly Type[] ReadOnlyInterfaces =
    [
        typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>), typeof(IReadOnlyDictionary<,>), typeof(IReadOnlySet<>), typeof(IEnumerable<>),
    ];

    /// <summary>Types whose instances cannot change, or that hold no data: values, strings,
    /// immutable and frozen collections, compiled patterns, reflection metadata, locks.</summary>
    private static bool IsImmutable(Type type)
    {
        lock (ImmutabilityLock)
        {
            if (Immutability.TryGetValue(type, out var known))
            {
                return known;
            }
            // Assume yes while inspecting (a type that refers to itself is judged by its other fields).
            Immutability[type] = true;
            var result = IsImmutableCore(type);
            Immutability[type] = result;
            return result;
        }
    }

    private static bool IsImmutableCore(Type type)
    {
        if (type.IsEnum || type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(DateOnly) ||
            type == typeof(TimeOnly) || type == typeof(Type) || type == typeof(object) || type == typeof(Lock) ||
            type == typeof(System.Text.RegularExpressions.Regex) || type == typeof(System.Text.CompositeFormat) ||
            type == typeof(StringComparer) || type == typeof(System.Globalization.CultureInfo) || type == typeof(System.Text.Encoding) ||
            typeof(MemberInfo).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type))
        {
            return true;
        }
        var ns = type.Namespace ?? "";
        if (ns == "System.Collections.Frozen" || ns == "System.Collections.Immutable" || type.Name.StartsWith("SearchValues", StringComparison.Ordinal))
        {
            return true;
        }
        if (Nullable.GetUnderlyingType(type) is { } inner)
        {
            return IsImmutable(inner);
        }
        if (type.IsValueType && type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true && !type.IsGenericType)
        {
            return true;
        }
        // Read-only collection contracts of immutable elements: the holder cannot change them
        // without a cast (arrays and concrete collections stay mutable).
        if (type.IsInterface && type.IsGenericType && ReadOnlyInterfaces.Contains(type.GetGenericTypeDefinition()))
        {
            return type.GetGenericArguments().All(IsImmutable);
        }
        // The product's own types (records, value objects, stateless services): immutable when every
        // instance field, including inherited ones, is read-only and immutable.
        if (type.Namespace?.StartsWith("Erp.", StringComparison.Ordinal) == true && !type.IsInterface && !type.IsAbstract)
        {
            for (var current = type; current is not null && current != typeof(object) && current != typeof(ValueType); current = current.BaseType)
            {
                if (current.Namespace?.StartsWith("Erp.", StringComparison.Ordinal) != true)
                {
                    // A framework base class (for example an EF Core interceptor) with no instance fields holds no state.
                    if (current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length > 0)
                    {
                        return false;
                    }
                    break;
                }
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!field.IsInitOnly || !IsImmutable(field.FieldType))
                    {
                        return false;
                    }
                }
            }
            return true;
        }
        return false;
    }

    private static string Describe(Type type) => type.IsArray ? $"array ({type.Name})" : type.IsGenericType
        ? $"{type.Name.Split('`')[0]}<{string.Join(", ", type.GetGenericArguments().Select(a => a.Name))}>"
        : type.Name;

    private static bool IsGenerated(Type type) =>
        type.Name.Contains('<', StringComparison.Ordinal) ||
        type.GetCustomAttribute<CompilerGeneratedAttribute>() is not null ||
        type.GetCustomAttribute<System.CodeDom.Compiler.GeneratedCodeAttribute>() is not null ||
        (type.Namespace?.StartsWith("System.Text.RegularExpressions.Generated", StringComparison.Ordinal) ?? false) ||
        (type.DeclaringType is { } declaring && IsGenerated(declaring));

    /// <summary>A static auto-property's backing field is reported as the property.</summary>
    private static string FieldName(FieldInfo field) =>
        field.Name.StartsWith('<') && field.Name.Contains(">k__BackingField", StringComparison.Ordinal)
            ? field.Name[1..field.Name.IndexOf('>', StringComparison.Ordinal)]
            : field.Name;

    private static string Name(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    private static IReadOnlyList<Assembly> ProductAssemblies()
    {
        var found = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var queue = new Queue<Assembly>([typeof(Program).Assembly]);
        while (queue.Count > 0)
        {
            var assembly = queue.Dequeue();
            if (!found.TryAdd(assembly.GetName().Name!, assembly))
            {
                continue;
            }
            foreach (var reference in assembly.GetReferencedAssemblies().Where(r => r.Name?.StartsWith("Erp.", StringComparison.Ordinal) == true))
            {
                queue.Enqueue(Assembly.Load(reference));
            }
        }
        return found.Values.OrderBy(a => a.GetName().Name, StringComparer.Ordinal).ToList();
    }

    private static bool IsProduct(Type type, IReadOnlyList<Assembly> assemblies) => assemblies.Contains(type.Assembly);

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.OfType<Type>();
        }
    }
}
