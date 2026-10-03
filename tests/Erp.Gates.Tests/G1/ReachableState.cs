using System.Reflection;
using System.Runtime.CompilerServices;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// State reachable from process-wide roots. A reviewed singleton or static field (for example the
/// module catalogue, filled once at start-up) is only as safe as everything it holds: the list
/// bindings, menus, module instances and registration lambdas hanging off it are shared by every
/// request of every tenant just like the root itself. A reviewed root is therefore never trusted
/// wholesale: this walks the live object graph from every product singleton instance and every
/// static field, through framework collections, arrays, delegates (to the closures and objects
/// they hold) and expression trees, and judges every field of every product object it reaches,
/// one field at a time. A cache added to any registration object (critic p05 round 1, plant L3: a
/// count cache inside the list binding) is a new field and fails the gate until it is removed or
/// reviewed on its own line.
/// </summary>
public static class ReachableState
{
    public sealed record Result(IReadOnlyList<ProcessStateFinding> Findings, int Roots, int ObjectsWalked, int TypesJudged, int ClosuresJudged);

    private const int MaxDepth = 64;
    private const int MaxObjects = 500_000;

    /// <param name="roots">Each root with the name used in findings (for example
    /// <c>singleton Erp.Kernel.Modules.ModuleCatalog</c>).</param>
    /// <param name="judgedElsewhere">Types whose own fields are judged by the caller (the singleton
    /// types); they are walked through but not reported twice.</param>
    public static Result Inspect(IEnumerable<(string Name, object? Value)> roots, IReadOnlyCollection<Assembly> product,
        IReadOnlySet<Type> serviceTypes, IReadOnlySet<Type> judgedElsewhere)
    {
        var walker = new Walker(product);
        var rootCount = 0;
        foreach (var (name, value) in roots)
        {
            rootCount++;
            walker.Walk(value, name, 0);
        }
        var findings = new List<ProcessStateFinding>();
        var types = 0;
        foreach (var (type, path) in walker.ProductObjects)
        {
            if (judgedElsewhere.Contains(type))
            {
                continue;
            }
            types++;
            for (var current = type; current is not null && current != typeof(object) && product.Contains(current.Assembly); current = current.BaseType)
            {
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly && product.Contains(field.FieldType.Assembly) && (field.FieldType.IsAbstract || field.FieldType.IsInterface))
                    {
                        // A read-only reference to a product contract (a module, a binding): the
                        // object it holds is reached and judged by its own fields.
                        continue;
                    }
                    if (ProcessState.Problem(field, serviceTypes) is { } why)
                    {
                        findings.Add(new ProcessStateFinding($"reachable {TypeName(current)}.{FieldName(field)}", $"{why}, reached from {path}"));
                    }
                }
            }
        }
        foreach (var (closure, path) in walker.Closures)
        {
            foreach (var finding in EndpointClosures.JudgeClosure(closure, serviceTypes, "reachable closure", $"shared by every tenant, reached from {path}"))
            {
                findings.Add(finding);
            }
        }
        return new Result(findings.DistinctBy(f => f.Key).OrderBy(f => f.Key, StringComparer.Ordinal).ToList(),
            rootCount, walker.Visited, types, walker.Closures.Count);
    }

    /// <summary>
    /// A fingerprint of everything reachable from the roots: one line per object or value reached,
    /// keyed by the first path that reached it (for example
    /// <c>singleton Erp.Kernel.Modules.ModuleCatalog._modules[1].ListBindings</c>), with the value,
    /// the collection size or the type. Taken before and after tenants use the app, any difference
    /// is state that changed under traffic: a cache, a counter, a remembered answer.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Fingerprint(IEnumerable<(string Name, object? Value)> roots, IReadOnlyCollection<Assembly> product)
    {
        var lines = new Dictionary<string, string>(StringComparer.Ordinal);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Note(string path, string summary)
        {
            if (lines.Count < MaxObjects)
            {
                lines.TryAdd(path, summary);
            }
        }
        void Walk(object? value, string path, int depth)
        {
            if (depth > MaxDepth || lines.Count >= MaxObjects)
            {
                return;
            }
            if (value is null)
            {
                Note(path, "null");
                return;
            }
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal || value is Guid || value is DateTime || value is DateTimeOffset || value is TimeSpan)
            {
                var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                Note(path, text.Length > 120 ? text[..120] + "…" : text);
                return;
            }
            if (value is Type or MemberInfo or IServiceProvider or Pointer or Module or Assembly)
            {
                Note(path, type.Name);
                return;
            }
            if (!type.IsValueType && !visited.Add(value))
            {
                return;
            }
            var isProduct = product.Contains(type.Assembly);
            Note(path, value is System.Collections.ICollection collection ? $"{TypeName(type)} count={collection.Count}" : TypeName(type));
            switch (value)
            {
                case Delegate @delegate:
                    Walk(@delegate.Target, path + " (target)", depth + 1);
                    return;
                case Array array:
                    if (!type.GetElementType()!.IsPrimitive)
                    {
                        var index = 0;
                        foreach (var item in array)
                        {
                            if (index > 100_000) break;
                            Walk(item, $"{path}[{index++}]", depth + 1);
                        }
                    }
                    return;
            }
            if (!isProduct && !IsContainer(type) && !(type.IsValueType && type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true && type.IsGenericType))
            {
                return;
            }
            for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            {
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType.IsPointer || field.FieldType.IsByRefLike)
                    {
                        continue;
                    }
                    object? child;
                    try
                    {
                        child = field.GetValue(value);
                    }
                    catch (Exception e) when (e is NotSupportedException or FieldAccessException or TargetException or ArgumentException)
                    {
                        continue;
                    }
                    Walk(child, path + "." + FieldName(field), depth + 1);
                }
            }
        }
        foreach (var (name, value) in roots)
        {
            Walk(value, name, 0);
        }
        return lines;
    }

    /// <summary>Lines of two fingerprints that differ (changed, added or gone).</summary>
    public static IReadOnlyList<string> Differences(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        var differences = new List<string>();
        foreach (var (path, was) in before)
        {
            if (!after.TryGetValue(path, out var now))
            {
                differences.Add($"{path}: was {was}, now gone");
            }
            else if (now != was)
            {
                differences.Add($"{path}: was {was}, now {now}");
            }
        }
        differences.AddRange(after.Where(p => !before.ContainsKey(p.Key)).Select(p => $"{p.Key}: new {p.Value}"));
        return differences;
    }

    /// <summary>A stable name for a type: generic definitions without their arguments
    /// (<c>Erp.Kernel.Lists.ListBinding`1</c>), nested types with dots.</summary>
    public static string TypeName(Type type)
    {
        var definition = type.IsGenericType && !type.IsGenericTypeDefinition ? type.GetGenericTypeDefinition() : type;
        return (definition.FullName ?? definition.Name).Replace('+', '.');
    }

    /// <summary>An auto-property's backing field is reported as the property.</summary>
    public static string FieldName(FieldInfo field) =>
        field.Name.StartsWith('<') && field.Name.Contains(">k__BackingField", StringComparison.Ordinal)
            ? field.Name[1..field.Name.IndexOf('>', StringComparison.Ordinal)]
            : field.Name;

    /// <summary>True for objects whose fields the walk follows although they are not the
    /// product's: collections, arrays, tuples and key/value pairs, lazies, expression trees and
    /// the closures compiled expressions keep their constants in.</summary>
    internal static bool IsContainer(Type type)
    {
        var name = type.FullName ?? type.Name;
        if (type.IsArray)
        {
            return true;
        }
        return name.StartsWith("System.Collections.", StringComparison.Ordinal) ||
               name.StartsWith("System.Linq.Expressions.", StringComparison.Ordinal) ||
               name.StartsWith("System.Runtime.CompilerServices.Closure", StringComparison.Ordinal) ||
               name.StartsWith("System.Runtime.CompilerServices.StrongBox", StringComparison.Ordinal) ||
               name.StartsWith("System.Lazy`", StringComparison.Ordinal) ||
               name.StartsWith("System.Tuple`", StringComparison.Ordinal) ||
               name.StartsWith("System.ValueTuple`", StringComparison.Ordinal) ||
               name.StartsWith("System.Collections.Generic.KeyValuePair`", StringComparison.Ordinal);
    }

    private sealed class Walker(IReadOnlyCollection<Assembly> product)
    {
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Type, string> _objects = [];
        private readonly Dictionary<Type, string> _closures = [];

        public IEnumerable<(Type Type, string Path)> ProductObjects => _objects.Select(p => (p.Key, p.Value));
        public IReadOnlyList<(Type Type, string Path)> Closures => _closures.Select(p => (p.Key, p.Value)).ToList();
        public int Visited => _visited.Count;

        public void Walk(object? value, string path, int depth)
        {
            if (value is null || depth > MaxDepth || _visited.Count > MaxObjects)
            {
                return;
            }
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is Type || value is MemberInfo || value is IServiceProvider ||
                value is Pointer || value is Module || value is Assembly)
            {
                return;
            }
            if (!type.IsValueType && !_visited.Add(value))
            {
                return;
            }
            var isProduct = product.Contains(type.Assembly);
            if (isProduct && IsGenerated(type))
            {
                if (EndpointClosures.IsClosureType(type))
                {
                    _closures.TryAdd(type, path);
                }
            }
            else if (isProduct)
            {
                _objects.TryAdd(type, path);
            }
            switch (value)
            {
                case Delegate @delegate:
                    Walk(@delegate.Target, path + " (delegate target)", depth + 1);
                    var invocations = @delegate.GetInvocationList();
                    if (invocations.Length > 1)
                    {
                        foreach (var invocation in invocations)
                        {
                            Walk(invocation, path, depth + 1);
                        }
                    }
                    return;
                case Array array:
                    if (!type.GetElementType()!.IsPrimitive)
                    {
                        var index = 0;
                        foreach (var item in array)
                        {
                            if (index > 100_000) break;
                            Walk(item, $"{path}[{index++}]", depth + 1);
                        }
                    }
                    return;
            }
            if (!isProduct && !IsContainer(type) && !(type.IsValueType && type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true && type.IsGenericType))
            {
                // Other framework objects (services, options, the data source) are not walked:
                // product services are roots of their own.
                return;
            }
            for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            {
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType.IsPointer || field.FieldType.IsByRefLike || field.FieldType.IsPrimitive)
                    {
                        continue;
                    }
                    object? child;
                    try
                    {
                        child = field.GetValue(value);
                    }
                    catch (Exception e) when (e is NotSupportedException or FieldAccessException or TargetException or ArgumentException)
                    {
                        continue;
                    }
                    var step = isProduct ? "." + FieldName(field) : "";
                    Walk(child, path + step, depth + 1);
                }
            }
        }
    }

    private static bool IsGenerated(Type type) =>
        type.Name.Contains('<', StringComparison.Ordinal) || type.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;
}
