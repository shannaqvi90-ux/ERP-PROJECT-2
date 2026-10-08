using System.Reflection;
using System.Reflection.Emit;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// Static fields of generic types. A generic type definition (<c>ListBinding&lt;T&gt;</c>) has no
/// static values of its own: each closed instantiation (<c>ListBinding&lt;User&gt;</c>) has its own
/// copy, which is as process-wide as any other static field (critic p05 round 4, plant L6: a
/// static memo in <c>ListBinding&lt;T&gt;</c> carried one tenant's count to another's next page
/// while the process-state gate skipped every generic type as a root). This finds the closed
/// instantiations of the product's generic types that the product names anywhere: in the IL of
/// every method, lambda and constructor, in field types, signatures and base types. The process-state
/// roots then include their static fields, and the reachable-state walk adds those of every
/// generic object it reaches.
/// </summary>
public static class GenericStatics
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Closed generic instantiations of product types named by the given types' code and
    /// signatures, whose definition is one of <paramref name="definitions"/>.</summary>
    public static IReadOnlyList<Type> Referenced(IEnumerable<Type> types, IReadOnlyCollection<Assembly> product, IReadOnlySet<Type> definitions)
    {
        var found = new HashSet<Type>();
        var seen = new HashSet<Type>();
        void Note(Type? type)
        {
            if (type is null || !seen.Add(type))
            {
                return;
            }
            if (type.HasElementType)
            {
                Note(type.GetElementType());
                return;
            }
            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    Note(argument);
                }
                if (!type.ContainsGenericParameters && product.Contains(type.Assembly) && definitions.Contains(type.GetGenericTypeDefinition()))
                {
                    found.Add(type);
                }
            }
            Note(type.DeclaringType);
            if (!type.IsGenericParameter)
            {
                Note(type.BaseType);
            }
        }
        foreach (var type in types)
        {
            Note(type.BaseType);
            foreach (var face in SafeInterfaces(type))
            {
                Note(face);
            }
            foreach (var field in type.GetFields(Declared))
            {
                Note(field.FieldType);
            }
            foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            {
                if (method is MethodInfo info)
                {
                    Note(info.ReturnType);
                }
                foreach (var parameter in SafeParameters(method))
                {
                    Note(parameter.ParameterType);
                }
                foreach (var named in TypesNamedIn(method))
                {
                    Note(named);
                }
            }
        }
        return found.OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
    }

    /// <summary>The live static fields of a closed generic type, each with a root name.</summary>
    public static IEnumerable<(string Name, object? Value)> Roots(Type closed)
    {
        foreach (var field in closed.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Where(f => !f.IsLiteral))
        {
            object? value;
            try
            {
                value = field.GetValue(null);
            }
            catch (Exception e) when (e is TypeInitializationException or TargetInvocationException or NotSupportedException or FieldAccessException or InvalidOperationException)
            {
                continue;
            }
            yield return ($"static {Display(closed)}.{ReachableState.FieldName(field)}", value);
        }
    }

    /// <summary>True when the type is a closed instantiation of a generic type (or nested in one)
    /// that declares static fields of its own.</summary>
    public static bool HasStatics(Type type) =>
        type.IsGenericType && !type.ContainsGenericParameters &&
        type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Any(f => !f.IsLiteral);

    /// <summary>A stable name with the type arguments (<c>Erp.Kernel.Lists.ListBinding`1[Erp.Modules.Identity.User]</c>).</summary>
    public static string Display(Type type) =>
        type.IsGenericType && !type.ContainsGenericParameters
            ? $"{ReachableState.TypeName(type)}[{string.Join(", ", type.GetGenericArguments().Select(Display))}]"
            : ReachableState.TypeName(type);

    private static Type[] SafeInterfaces(Type type)
    {
        try
        {
            return type.GetInterfaces();
        }
        catch (Exception e) when (e is TypeLoadException or FileNotFoundException)
        {
            return [];
        }
    }

    private static ParameterInfo[] SafeParameters(MethodBase method)
    {
        try
        {
            return method.GetParameters();
        }
        catch (Exception e) when (e is TypeLoadException or FileNotFoundException)
        {
            return [];
        }
    }

    /// <summary>Every type a method's IL names: the declaring types of the fields it reads or
    /// writes and of the methods it calls, and the types it creates, casts to or loads.</summary>
    private static IEnumerable<Type> TypesNamedIn(MethodBase method)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception e) when (e is InvalidOperationException or BadImageFormatException or TypeLoadException)
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
            if (opCode.OperandType is not (OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok))
            {
                continue;
            }
            MemberInfo? member = null;
            try
            {
                member = method.Module.ResolveMember(BitConverter.ToInt32(il, operandStart), typeArgs, methodArgs);
            }
            catch (Exception e) when (e is ArgumentException or BadImageFormatException or TypeLoadException or FileNotFoundException or MissingMemberException)
            {
            }
            switch (member)
            {
                case Type type:
                    yield return type;
                    break;
                case MethodInfo called:
                    if (called.DeclaringType is { } declaring)
                    {
                        yield return declaring;
                    }
                    if (called.IsGenericMethod)
                    {
                        foreach (var argument in called.GetGenericArguments())
                        {
                            yield return argument;
                        }
                    }
                    break;
                case { DeclaringType: { } owner }:
                    yield return owner;
                    break;
            }
        }
    }
}
