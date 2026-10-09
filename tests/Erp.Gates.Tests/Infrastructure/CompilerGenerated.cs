using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// Which types the compiler made rather than a person wrote: closures (display classes), lambda
/// caches, iterator and async state machines, anonymous types, fixed and inline buffers, and the
/// code of source generators. The process-state gates skip these as roots (their state is reached
/// through what holds them) and walk them as closures.
///
/// A name with '&lt;' in it is not enough (critic p05 round 7, plant L11): a C# file-local type
/// (<c>file static class Memo</c>) compiles to a name such as <c>&lt;ListBinding&gt;F3A…__Memo</c>, and
/// skipping it hid a tenant-blind static memo from every process-state gate. A file-local type is
/// written by a person and judged like any other, unless a source generator made it (it then
/// carries <see cref="GeneratedCodeAttribute"/>, like the regular-expression generator's helpers).
/// </summary>
public static partial class CompilerGenerated
{
    public static bool Is(Type type)
    {
        if (type.GetCustomAttribute<CompilerGeneratedAttribute>() is not null || type.GetCustomAttribute<GeneratedCodeAttribute>() is not null)
        {
            return true;
        }
        if (type.Namespace?.StartsWith("System.Text.RegularExpressions.Generated", StringComparison.Ordinal) ?? false)
        {
            return true;
        }
        // Compiler names that start with '<' ("<>c", "<>c__DisplayClass0_0", "<Run>d__3",
        // "<>f__AnonymousType0", "<PrivateImplementationDetails>"), except file-local types.
        return type.Name.StartsWith('<') && !IsFileLocal(type);
    }

    /// <summary>A C# file-local type: <c>&lt;FileName&gt;F{checksum}__{Name}</c>.</summary>
    public static bool IsFileLocal(Type type) => FileLocalName().IsMatch(type.Name);

    [GeneratedRegex(@"^<[^<>]*>F[0-9A-Fa-f]+__[^<>]+$")]
    private static partial Regex FileLocalName();
}
