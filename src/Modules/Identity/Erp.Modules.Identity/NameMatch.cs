using System.Text;

namespace Erp.Modules.Identity;

/// <summary>Builds the PostgreSQL regular expression that finds a typed name in an Arabic name the way a
/// reader would: the alef forms are one letter, ya and alef maqsura are one letter, ta marbuta and ha are
/// one letter, and diacritics and tatweel between letters are ignored.</summary>
internal static class NameMatch
{
    internal enum Position { Anywhere, Start, Whole }

    private const string Marks = "[\u064B-\u0652\u0640]*";

    private static string? Class(char c) => c switch
    {
        '\u0627' or '\u0623' or '\u0625' or '\u0622' or '\u0671' => "[\u0627\u0623\u0625\u0622\u0671]",
        '\u0649' or '\u064A' or '\u06CC' => "[\u0649\u064A\u06CC]",
        '\u0629' or '\u0647' => "[\u0629\u0647]",
        _ => null,
    };

    internal static string Pattern(string text, Position position)
    {
        var sb = new StringBuilder(position == Position.Anywhere ? "" : "^");
        var lastWasSpace = false;
        foreach (var c in text)
        {
            if (c is >= '\u064B' and <= '\u0652' or '\u0640')
            {
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append("\\s+");
                }
                lastWasSpace = true;
                continue;
            }
            lastWasSpace = false;
            if (char.IsSurrogate(c))
            {
                // A character outside the Basic Multilingual Plane (an emoji) is two halves that must stay one pair: never escaped or split.
                sb.Append(c);
                continue;
            }
            sb.Append(Class(c) ?? (char.IsLetterOrDigit(c) ? c.ToString() : "\\" + c)).Append(Marks);
        }
        if (position == Position.Whole)
        {
            sb.Append('$');
        }
        return sb.ToString();
    }
}
