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
            $"{inventory.EndpointsWalked} endpoint delegates walked ({inventory.DelegateObjectsWalked} objects) to {inventory.ClosuresInspected} closures; " +
            $"{inventory.FrameworkSingletonsInspected} framework singletons over product types; {inventory.ReachableRoots} roots walked to {inventory.ReachableObjectsWalked} objects, {inventory.ReachableTypesJudged} product types judged field by field; {inventory.Findings.Count} reviewed findings");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.False(inventory.ReachableWalkCut, $"the reachable-state walk stopped at its object budget after {inventory.ReachableObjectsWalked} objects; what it did not reach was not judged");
        Assert.True(inventory.FieldsInspected >= Ratchet.Min("g1.processStateFieldsInspected"),
            $"g1.processStateFieldsInspected: {inventory.FieldsInspected}; ratchet minimum {Ratchet.Min("g1.processStateFieldsInspected")}");
        Assert.True(inventory.SingletonsInspected >= Ratchet.Min("g1.singletonsInspected"),
            $"g1.singletonsInspected: {inventory.SingletonsInspected}; ratchet minimum {Ratchet.Min("g1.singletonsInspected")}");
        Assert.True(inventory.EndpointsWalked >= Ratchet.Min("g1.endpointDelegatesWalked"),
            $"g1.endpointDelegatesWalked: {inventory.EndpointsWalked}; ratchet minimum {Ratchet.Min("g1.endpointDelegatesWalked")}");
        Assert.True(inventory.ClosuresInspected >= Ratchet.Min("g1.endpointClosuresInspected"),
            $"g1.endpointClosuresInspected: {inventory.ClosuresInspected}; ratchet minimum {Ratchet.Min("g1.endpointClosuresInspected")}");
        Assert.True(inventory.ReachableObjectsWalked >= Ratchet.Min("g1.reachableObjectsWalked"),
            $"g1.reachableObjectsWalked: {inventory.ReachableObjectsWalked}; ratchet minimum {Ratchet.Min("g1.reachableObjectsWalked")}");
        Assert.True(inventory.ReachableTypesJudged >= Ratchet.Min("g1.reachableTypesJudged"),
            $"g1.reachableTypesJudged: {inventory.ReachableTypesJudged}; ratchet minimum {Ratchet.Min("g1.reachableTypesJudged")}");
        Assert.True(inventory.FrameworkSingletonsInspected >= Ratchet.Min("g1.frameworkSingletonsInspected"),
            $"g1.frameworkSingletonsInspected: {inventory.FrameworkSingletonsInspected}; ratchet minimum {Ratchet.Min("g1.frameworkSingletonsInspected")}");
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

    /// <summary>Framework singletons built over the product's types (or bare framework collections)
    /// found, reported and walked as roots.</summary>
    public int FrameworkSingletonsInspected { get; init; }

    /// <summary>Singleton instances and static fields whose object graphs were walked.</summary>
    public int ReachableRoots { get; init; }

    /// <summary>Objects reached from those roots (registrations, bindings, collections, closures).</summary>
    public int ReachableObjectsWalked { get; init; }

    /// <summary>Product types reached from those roots whose fields were judged one by one.</summary>
    public int ReachableTypesJudged { get; init; }

    /// <summary>True when the walk stopped at its object budget, so some roots or objects were not
    /// judged (the gate fails: a walk that stops early is blind to what it did not reach).</summary>
    public bool ReachableWalkCut { get; init; }
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

    /// <summary>The product's assemblies, the app's service types and its product singleton types.</summary>
    public sealed record ProductContext(IReadOnlyList<Assembly> Assemblies, IReadOnlyList<ServiceDescriptor> Descriptors, IReadOnlySet<Type> ServiceTypes, IReadOnlyList<Type> Singletons)
    {
        /// <summary>Singletons whose implementation is a framework type built over the product's
        /// types or a bare framework collection (critic p05 round 5, plant L10: an
        /// <c>ObjectPool&lt;ListPageScratch&gt;</c> whose implementation, the framework's
        /// <c>DefaultObjectPool&lt;T&gt;</c>, held never-cleared product objects that no product
        /// singleton or static field reached).</summary>
        public IReadOnlyList<FrameworkSingleton> FrameworkHolders { get; init; } = [];
    }

    /// <summary>A framework singleton that can hold the product's objects: its service type, the
    /// live instance, and why it counts.</summary>
    public sealed record FrameworkSingleton(Type ServiceType, object Instance, string Why)
    {
        public string Key => $"framework-singleton {Readable(ServiceType)}";
    }

    public static ProductContext ContextOf(ErpAppFactory factory)
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
        return new ProductContext(assemblies, descriptors, serviceTypes, singletons.Distinct().ToList()) { FrameworkHolders = FrameworkHoldersOf(factory, descriptors, assemblies) };
    }

    /// <summary>
    /// Singleton registrations served by a framework type that can hold the product's objects: the
    /// service or implementation type is built over a product type (an object pool, a cache, a
    /// channel, a collection of the product's records, at any depth of generic arguments), or it is
    /// a bare framework collection. Their state is as process-wide as a product singleton's but no
    /// product field holds them, so they are roots of their own: each is reported (it needs a
    /// reviewed reason) and walked, framework internals included, down to the product objects it
    /// holds.
    /// </summary>
    private static List<FrameworkSingleton> FrameworkHoldersOf(ErpAppFactory factory, IReadOnlyList<ServiceDescriptor> descriptors, IReadOnlyList<Assembly> assemblies)
    {
        var found = new List<FrameworkSingleton>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var descriptor in descriptors.Where(d => d.Lifetime == ServiceLifetime.Singleton && !d.IsKeyedService && !d.ServiceType.ContainsGenericParameters))
        {
            var declared = descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
            if (declared is not null && IsProduct(declared, assemblies))
            {
                continue;
            }
            var why = Carries(descriptor.ServiceType, assemblies, 0) ?? (declared is null ? null : Carries(declared, assemblies, 0));
            if (why is null && !(ReachableState.IsContainer(descriptor.ServiceType) || (declared is not null && ReachableState.IsContainer(declared))))
            {
                continue;
            }
            object? instance;
            try
            {
                instance = descriptor.ImplementationInstance ?? factory.Services.GetService(descriptor.ServiceType);
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            if (instance is null || IsProduct(instance.GetType(), assemblies) || !seen.Add(instance))
            {
                continue;
            }
            why ??= Carries(instance.GetType(), assemblies, 0) ?? $"a framework collection ({Describe(instance.GetType())})";
            found.Add(new FrameworkSingleton(descriptor.ServiceType, instance, why));
        }
        return found;
    }

    /// <summary>Why a framework type can hold the product's objects (a product type among its
    /// generic arguments or array elements, at any depth), or null.</summary>
    private static string? Carries(Type type, IReadOnlyList<Assembly> assemblies, int depth)
    {
        if (depth > 8)
        {
            return null;
        }
        if (type.IsArray)
        {
            var element = type.GetElementType()!;
            return IsProduct(element, assemblies) ? $"built over the product's {Readable(element)}" : Carries(element, assemblies, depth + 1);
        }
        if (!type.IsGenericType)
        {
            return null;
        }
        foreach (var argument in type.GetGenericArguments())
        {
            if (IsProduct(argument, assemblies))
            {
                return $"built over the product's {Readable(argument)}";
            }
            if (Carries(argument, assemblies, depth + 1) is { } inner)
            {
                return inner;
            }
        }
        return null;
    }

    /// <summary>A type's name with its generic arguments spelled out (<c>Microsoft.Extensions.ObjectPool.ObjectPool&lt;Erp.X.Scratch&gt;</c>).</summary>
    public static string Readable(Type type)
    {
        if (type.IsArray)
        {
            return Readable(type.GetElementType()!) + "[]";
        }
        if (!type.IsGenericType)
        {
            return (type.FullName ?? type.Name).Replace('+', '.');
        }
        var definition = type.GetGenericTypeDefinition();
        var name = (definition.FullName ?? definition.Name).Replace('+', '.');
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return $"{(tick < 0 ? name : name[..tick])}<{string.Join(", ", type.GetGenericArguments().Select(Readable))}>";
    }

    /// <summary>Every live root of process-wide state in the running app (product singleton
    /// instances and static field values) with the product's assemblies.</summary>
    public static (IReadOnlyList<(string Name, object? Value)> Roots, IReadOnlyList<Assembly> Assemblies) LiveRoots(ErpAppFactory factory)
    {
        var context = ContextOf(factory);
        var types = context.Assemblies.SelectMany(LoadableTypes).Where(t => !Instrument(t));
        return (Roots(factory, context.Descriptors, context.Singletons, types).Concat(HolderRoots(context)).ToList(), context.Assemblies);
    }

    /// <summary>The framework singletons that can hold the product's objects, as roots whose own
    /// framework fields are followed (<see cref="ReachableState.FrameworkHolder"/>).</summary>
    private static IEnumerable<(string Name, object? Value)> HolderRoots(ProductContext context) =>
        context.FrameworkHolders.Select(h => ($"singleton {Readable(h.ServiceType)}", (object?)new ReachableState.FrameworkHolder(h.Instance)));

    /// <summary>
    /// The gate's own instruments (the SQL trace, the attack's bookkeeping) change while they
    /// measure; when the gate assembly is loaded as a module (the self-tests' planted modules)
    /// only its planted code is the app's. In the product's own environment the gate assembly is
    /// not a product assembly at all, so this changes nothing there.
    /// </summary>
    private static bool Instrument(Type type) =>
        type.Namespace?.StartsWith("Erp.Gates.Tests", StringComparison.Ordinal) == true &&
        type.Namespace?.StartsWith("Erp.Gates.Tests.SelfTests", StringComparison.Ordinal) != true;

    public static ProcessStateInventory Inspect(ErpAppFactory factory)
    {
        var context = ContextOf(factory);
        var (assemblies, descriptors, serviceTypes, singletons) = context;
        var productTypes = assemblies.SelectMany(LoadableTypes).ToList();
        var inventory = InspectTypes(productTypes, singletons, serviceTypes);
        var findings = inventory.Findings.ToList();
        // Everything those singletons and static fields hold, judged field by field: a reviewed
        // root is never trusted for what hangs off it (list bindings, menus, module instances).
        // The roots are the app's (as for the fingerprint, LiveRoots): the gate's own instruments
        // are not walked as roots. The SQL trace holds every call of every test in the process, so
        // in a full run it could fill the walk's object budget before the app's last roots (the
        // statics of generic instantiations) were reached, and a planted memo went unjudged.
        var reachable = ReachableState.Inspect(Roots(factory, descriptors, singletons, productTypes.Where(t => !Instrument(t))).Concat(HolderRoots(context)),
            assemblies, serviceTypes, singletons.ToHashSet());
        // A framework singleton that can hold the product's objects is process-wide state like a
        // product singleton: it needs a reviewed reason (and what it holds is judged by the walk).
        findings.AddRange(context.FrameworkHolders.Select(h => new ProcessStateFinding(h.Key,
            $"a framework singleton ({Describe(h.Instance.GetType())}) {h.Why}: whatever it keeps lives as long as the process, shared by every tenant")));
        findings.AddRange(reachable.Findings);
        inventory = inventory with { FrameworkSingletonsInspected = context.FrameworkHolders.Count, ReachableRoots = reachable.Roots, ReachableObjectsWalked = reachable.ObjectsWalked, ReachableTypesJudged = reachable.TypesJudged, ReachableWalkCut = reachable.Cut };
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

    /// <summary>The live roots of process-wide state: every product singleton instance the app
    /// holds and the value of every static field of the product's types.</summary>
    private static IEnumerable<(string Name, object? Value)> Roots(ErpAppFactory factory, IReadOnlyList<ServiceDescriptor> descriptors,
        IReadOnlyCollection<Type> singletons, IEnumerable<Type> productTypes)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var descriptor in descriptors.Where(d => d.Lifetime == ServiceLifetime.Singleton && !d.IsKeyedService && !d.ServiceType.ContainsGenericParameters))
        {
            object?[] candidates;
            try
            {
                candidates = descriptor.ImplementationInstance is { } instance ? [instance] : factory.Services.GetServices(descriptor.ServiceType).ToArray();
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            foreach (var candidate in candidates)
            {
                if (candidate is not null && singletons.Contains(candidate.GetType()) && seen.Add(candidate))
                {
                    yield return ($"singleton {Name(candidate.GetType())}", candidate);
                }
            }
        }
        var listed = productTypes.ToList();
        foreach (var type in listed.Where(t => !t.ContainsGenericParameters && !IsGenerated(t)))
        {
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Where(f => !f.IsLiteral))
            {
                object? value;
                try
                {
                    value = field.GetValue(null);
                }
                catch (Exception e) when (e is TypeInitializationException or TargetInvocationException or NotSupportedException or FieldAccessException)
                {
                    continue;
                }
                yield return ($"static {Name(type)}.{FieldName(field)}", value);
            }
        }
        // Each closed instantiation of a generic type has static fields of its own (critic p05
        // round 4, plant L6): those the product's code names are roots too. Instantiations reached
        // only as objects are added by the reachable-state walk.
        var definitions = listed.Where(t => t.IsGenericTypeDefinition && !IsGenerated(t)).ToHashSet();
        foreach (var closed in GenericStatics.Referenced(listed, listed.Select(t => t.Assembly).Distinct().ToList(), definitions))
        {
            foreach (var root in GenericStatics.Roots(closed))
            {
                yield return root;
            }
        }
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
                else if (typeof(Delegate).IsAssignableFrom(field.FieldType))
                {
                    // A delegate cannot change, but what its closure captured can (critic p05 round 4,
                    // plant L6: a static Func over a dictionary of counts, in a generic type).
                    findings.Add(new ProcessStateFinding($"static {Name(type)}.{FieldName(field)}",
                        $"a static delegate field ({Describe(field.FieldType)}): whatever its closure captures lives as long as the process, shared by every tenant"));
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
        // Every array can have its elements replaced, whatever their type (critic p04 round 1:
        // an endpoint lambda captured a SessionUser?[] and passed the previous caller on in it;
        // an array of a product record was judged by the record's fields and taken as immutable).
        if (type.IsArray || type.IsPointer || type.IsByRef)
        {
            return false;
        }
        if (type.IsEnum || type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(DateOnly) ||
            type == typeof(TimeOnly) || type == typeof(Type) || type == typeof(object) || type == typeof(Lock) ||
            type == typeof(System.Text.RegularExpressions.Regex) || type == typeof(System.Text.CompositeFormat) ||
            type == typeof(StringComparer) || type == typeof(System.Globalization.CultureInfo) || type == typeof(System.Text.Encoding) ||
            typeof(MemberInfo).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type) ||
            // Expression trees cannot change once built; the constants they hold are walked by
            // ReachableState like any other object.
            typeof(System.Linq.Expressions.Expression).IsAssignableFrom(type))
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
        CompilerGenerated.Is(type) || (type.DeclaringType is { } declaring && IsGenerated(declaring));

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
