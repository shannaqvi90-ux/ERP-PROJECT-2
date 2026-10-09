using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Erp.Gates.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// State captured by endpoint delegates. A lambda passed to <c>MapGet</c> and friends is created
/// once, when the endpoints are mapped, and lives as long as the process: every variable it
/// captures (a compiler-generated closure field) and every object it reaches through its
/// <c>this</c> is shared by every request of every tenant, exactly like a static field. This walks
/// each endpoint's request delegate, through the framework's own closures, to the product's
/// closures and objects it holds, and reports every captured variable that the lambdas write
/// (found in their IL) and every captured mutable object.
/// </summary>
public static class EndpointClosures
{
    public sealed record Result(IReadOnlyList<ProcessStateFinding> Findings, int Endpoints, int Closures, int ObjectsWalked);

    private const int MaxDepth = 24;
    private const int MaxObjects = 400_000;

    public static Result Inspect(IServiceProvider services, IReadOnlyCollection<Assembly> product, IReadOnlySet<Type> serviceTypes)
    {
        var walker = new Walker(product);
        var endpoints = 0;
        foreach (var endpoint in services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints)
        {
            endpoints++;
            walker.Walk(endpoint.RequestDelegate, 0);
            foreach (var item in endpoint.Metadata)
            {
                if (item is Delegate metadataDelegate)
                {
                    walker.Walk(metadataDelegate, 0);
                }
            }
        }

        var findings = new List<ProcessStateFinding>();
        foreach (var closure in walker.Closures.Select(c => c.GetType()).Distinct())
        {
            findings.AddRange(JudgeClosure(closure, serviceTypes, "closure", "shared by every request of every tenant"));
        }
        foreach (var held in walker.ProductObjects.Select(o => o.GetType()).Distinct())
        {
            for (var current = held; current is not null && current != typeof(object); current = current.BaseType)
            {
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (ProcessState.Problem(field, serviceTypes) is { } why)
                    {
                        findings.Add(new ProcessStateFinding($"endpoint-held {Name(held)}.{field.Name}", why + ", held by an endpoint delegate"));
                    }
                }
            }
        }
        return new Result(findings.DistinctBy(f => f.Key).ToList(), endpoints, walker.Closures.Count, walker.Visited);
    }

    /// <summary>The captured variables of a closure that are written inside its lambdas, or that
    /// hold a mutable object: one copy lives as long as whatever holds the closure.</summary>
    internal static IEnumerable<ProcessStateFinding> JudgeClosure(Type closure, IReadOnlySet<Type> serviceTypes, string prefix, string sharedBy)
    {
        var written = WrittenInsideLambdas(closure);
        var outer = Outermost(closure);
        var enclosing = EnclosingMethod(closure);
        foreach (var field in closure.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (field.Name.StartsWith('<'))
            {
                // <>4__this and CS$<>8__locals: links to the enclosing object or closure, walked
                // and judged as objects of their own.
                continue;
            }
            var key = $"{prefix} {Name(outer)}.{enclosing}.{field.Name}";
            if (written.Contains((field.Module, field.MetadataToken)))
            {
                yield return new ProcessStateFinding(key,
                    $"a captured variable written inside its lambda ({Describe(field.FieldType)}): one copy {sharedBy}");
            }
            else if (!IsService(field.FieldType, serviceTypes) && !ProcessState.IsImmutableType(field.FieldType))
            {
                yield return new ProcessStateFinding(key, $"a captured mutable {Describe(field.FieldType)} {sharedBy}");
            }
        }
    }

    /// <summary>A compiler-generated class holding captured variables (a display class).</summary>
    internal static bool IsClosureType(Type type) => IsClosure(type);

    private static bool IsService(Type type, IReadOnlySet<Type> serviceTypes) =>
        serviceTypes.Contains(type) || type == typeof(IServiceProvider) || (type.IsGenericType && serviceTypes.Contains(type.GetGenericTypeDefinition()));

    private sealed class Walker(IReadOnlyCollection<Assembly> product)
    {
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

        public List<object> Closures { get; } = [];
        public List<object> ProductObjects { get; } = [];
        public int Visited => _visited.Count;

        public void Walk(object? value, int depth)
        {
            if (value is null || depth > MaxDepth || _visited.Count > MaxObjects)
            {
                return;
            }
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is Type || value is MemberInfo || value is IServiceProvider ||
                value is HttpContext || value is Pointer)
            {
                return;
            }
            if (!type.IsValueType && !_visited.Add(value))
            {
                return;
            }
            var isProduct = product.Contains(type.Assembly);
            if (isProduct && IsClosure(type))
            {
                Closures.Add(value);
            }
            else if (isProduct && !IsGenerated(type))
            {
                // An object an endpoint delegate holds (for example through a captured `this`):
                // judged by its own fields, not walked further (services are inspected elsewhere).
                ProductObjects.Add(value);
                return;
            }
            switch (value)
            {
                case Delegate @delegate:
                    Walk(@delegate.Target, depth + 1);
                    var invocations = @delegate.GetInvocationList();
                    if (invocations.Length > 1)
                    {
                        foreach (var invocation in invocations)
                        {
                            Walk(invocation, depth + 1);
                        }
                    }
                    return;
                case Array array:
                    if (!array.GetType().GetElementType()!.IsPrimitive)
                    {
                        var count = 0;
                        foreach (var item in array)
                        {
                            if (++count > 10_000) break;
                            Walk(item, depth + 1);
                        }
                    }
                    return;
            }
            if (!Descend(type, isProduct))
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
                    catch (Exception e) when (e is NotSupportedException or FieldAccessException or TargetException)
                    {
                        continue;
                    }
                    Walk(child, depth + 1);
                }
            }
        }

        /// <summary>Objects that make up delegates: compiler closures (product or framework),
        /// expression-tree closures, and the framework's request-delegate machinery.</summary>
        private static bool Descend(Type type, bool isProduct)
        {
            if (isProduct || IsGenerated(type))
            {
                return true;
            }
            var name = type.FullName ?? "";
            return name.StartsWith("System.Runtime.CompilerServices.Closure", StringComparison.Ordinal) ||
                   name.StartsWith("System.Runtime.CompilerServices.StrongBox", StringComparison.Ordinal) ||
                   name.StartsWith("Microsoft.AspNetCore.Http.", StringComparison.Ordinal) ||
                   name.StartsWith("Microsoft.AspNetCore.Routing.", StringComparison.Ordinal) ||
                   name.StartsWith("Microsoft.AspNetCore.Builder.", StringComparison.Ordinal) ||
                   name.StartsWith("Microsoft.Extensions.Internal.", StringComparison.Ordinal);
        }
    }

    private static bool IsGenerated(Type type) => CompilerGenerated.Is(type);

    /// <summary>A compiler-generated class holding captured variables (a display class).</summary>
    private static bool IsClosure(Type type) =>
        IsGenerated(type) && !typeof(IAsyncStateMachine).IsAssignableFrom(type) && type.Name.Contains("DisplayClass", StringComparison.Ordinal);

    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }
        return type;
    }

    /// <summary>The method whose lambdas the closure serves: <c>Register</c> for
    /// <c>&lt;Register&gt;b__2</c>.</summary>
    private static string EnclosingMethod(Type closure)
    {
        foreach (var method in closure.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (method.Name.StartsWith('<') && method.Name.IndexOf('>', StringComparison.Ordinal) is var end and > 1)
            {
                return method.Name[1..end];
            }
        }
        return "lambda";
    }

    /// <summary>Fields of the closure stored (or whose address is taken) by code that runs as a
    /// lambda or local function: methods of the closure itself and of the types nested in it
    /// (async state machines), and of the other closures of the same class. The method that
    /// creates the closure and fills it is ordinary code and does not count.</summary>
    private static HashSet<(Module, int)> WrittenInsideLambdas(Type closure)
    {
        var outer = Outermost(closure);
        var lambdaTypes = new List<Type>();
        void Collect(Type type, bool insideLambda)
        {
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                // Async state machines of ordinary methods (directly in a non-generated class) are
                // the creating method itself; everything else generated is a lambda body.
                var isLambda = insideLambda || (IsGenerated(nested) && !(typeof(IAsyncStateMachine).IsAssignableFrom(nested) && !IsGenerated(type)));
                if (isLambda)
                {
                    lambdaTypes.Add(nested);
                }
                Collect(nested, isLambda);
            }
        }
        Collect(outer, false);
        var written = new HashSet<(Module, int)>();
        foreach (var type in lambdaTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                foreach (var field in Il.FieldsWritten(method))
                {
                    if (field.DeclaringType == closure || (field.DeclaringType?.IsGenericType == true && field.DeclaringType.GetGenericTypeDefinition() == closure))
                    {
                        written.Add((field.Module, field.MetadataToken));
                    }
                }
            }
        }
        return written;
    }

    private static string Name(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    private static string Describe(Type type) => type.IsGenericType
        ? $"{type.Name.Split('`')[0]}<{string.Join(", ", type.GetGenericArguments().Select(a => a.Name))}>"
        : type.Name;

    /// <summary>A minimal IL reader: the fields a method stores to or takes the address of.</summary>
    private static class Il
    {
        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value);

        public static IEnumerable<FieldInfo> FieldsWritten(MethodInfo method)
        {
            byte[]? il;
            try
            {
                il = method.GetMethodBody()?.GetILAsByteArray();
            }
            catch (Exception e) when (e is InvalidOperationException or BadImageFormatException)
            {
                yield break;
            }
            if (il is null)
            {
                yield break;
            }
            var typeArgs = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
            var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
            var position = 0;
            while (position < il.Length)
            {
                short value = il[position++];
                if (value == 0xFE && position < il.Length)
                {
                    value = (short)(0xFE00 | il[position++]);
                }
                if (!OpCodesByValue.TryGetValue(value, out var opCode))
                {
                    yield break;
                }
                var operandStart = position;
                position += opCode.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, operandStart),
                    _ => 4,
                };
                if (opCode == OpCodes.Stfld || opCode == OpCodes.Ldflda || opCode == OpCodes.Stsfld || opCode == OpCodes.Ldsflda)
                {
                    FieldInfo? field = null;
                    try
                    {
                        field = method.Module.ResolveField(BitConverter.ToInt32(il, operandStart), typeArgs, methodArgs);
                    }
                    catch (Exception e) when (e is ArgumentException or BadImageFormatException)
                    {
                    }
                    if (field is not null)
                    {
                        yield return field;
                    }
                }
            }
        }
    }
}
