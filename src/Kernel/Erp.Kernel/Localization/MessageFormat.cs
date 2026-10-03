using System.Globalization;
using System.Text;

namespace Erp.Kernel.Localization;

/// <summary>
/// The plural categories of the supported languages, from the Unicode CLDR plural rules
/// (https://www.unicode.org/cldr/charts/latest/supplemental/language_plural_rules.html).
/// English: one, other. Arabic: zero, one, two, few, many, other.
/// </summary>
public static class PluralRules
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Categories = new Dictionary<string, IReadOnlyList<string>>
    {
        [Languages.English] = ["one", "other"],
        [Languages.Arabic] = ["zero", "one", "two", "few", "many", "other"],
    };

    /// <summary>The plural category of a number in a language (integers and decimals).</summary>
    public static string Select(string language, decimal number)
    {
        var n = Math.Abs(number);
        var isInteger = n == decimal.Truncate(n);
        if (language == Languages.Arabic)
        {
            if (!isInteger) return "other";
            var mod100 = n % 100;
            return n switch
            {
                0 => "zero",
                1 => "one",
                2 => "two",
                _ when mod100 >= 3 && mod100 <= 10 => "few",
                _ when mod100 >= 11 && mod100 <= 99 => "many",
                _ => "other",
            };
        }
        return isInteger && n == 1 ? "one" : "other";
    }
}

/// <summary>
/// A small subset of ICU MessageFormat shared by every module's strings: <c>{name}</c> (or
/// <c>{0}</c>) placeholders and plurals, <c>{count, plural, =0 {…} one {# item} other {# items}}</c>,
/// where <c>#</c> is the number. Arabic needs all six plural forms; the string gate checks every
/// plural message carries the categories its language needs.
/// </summary>
public static class MessageFormat
{
    public static string Format(string message, string language, Func<string, object?> valueOf)
    {
        var output = new StringBuilder();
        Append(output, message, language, valueOf, pound: null);
        return output.ToString();
    }

    public static string Format(string message, string language, params object?[] args) =>
        Format(message, language, name => int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < args.Length ? args[i] : null);

    /// <summary>The plural messages in a text: variable name and the selectors it covers.</summary>
    public static IReadOnlyList<(string Variable, IReadOnlyList<string> Selectors)> Plurals(string message)
    {
        var found = new List<(string, IReadOnlyList<string>)>();
        var i = 0;
        while (i < message.Length)
        {
            if (message[i] == '{')
            {
                var end = Matching(message, i);
                var body = message[(i + 1)..end];
                var parts = body.Split(',', 3);
                if (parts.Length == 3 && parts[1].Trim() == "plural")
                {
                    var branches = Branches(parts[2]);
                    found.Add((parts[0].Trim(), branches.Select(b => b.Selector).ToList()));
                    foreach (var branch in branches)
                    {
                        found.AddRange(Plurals(branch.Text));
                    }
                }
                i = end + 1;
                continue;
            }
            i++;
        }
        return found;
    }

    /// <summary>Simple placeholders (not plurals) in a text.</summary>
    public static IReadOnlySet<string> Placeholders(string message)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var i = 0;
        while (i < message.Length)
        {
            if (message[i] == '{')
            {
                var end = Matching(message, i);
                var body = message[(i + 1)..end];
                var parts = body.Split(',', 3);
                if (parts.Length == 3 && parts[1].Trim() == "plural")
                {
                    found.Add(parts[0].Trim());
                    foreach (var branch in Branches(parts[2]))
                    {
                        found.UnionWith(Placeholders(branch.Text));
                    }
                }
                else
                {
                    found.Add(body.Trim());
                }
                i = end + 1;
                continue;
            }
            i++;
        }
        return found;
    }

    private static void Append(StringBuilder output, string message, string language, Func<string, object?> valueOf, string? pound)
    {
        var i = 0;
        while (i < message.Length)
        {
            var c = message[i];
            if (c == '#' && pound is not null)
            {
                output.Append(pound);
                i++;
                continue;
            }
            if (c != '{')
            {
                output.Append(c);
                i++;
                continue;
            }
            var end = Matching(message, i);
            if (end < 0)
            {
                output.Append(message, i, message.Length - i);
                return;
            }
            var body = message[(i + 1)..end];
            var parts = body.Split(',', 3);
            if (parts.Length == 3 && parts[1].Trim() == "plural")
            {
                var value = valueOf(parts[0].Trim());
                var number = ToDecimal(value);
                var branches = Branches(parts[2]);
                var exact = branches.FirstOrDefault(b => b.Selector == "=" + number.ToString(CultureInfo.InvariantCulture));
                var category = PluralRules.Select(language, number);
                var chosen = exact.Text ?? branches.FirstOrDefault(b => b.Selector == category).Text ?? branches.FirstOrDefault(b => b.Selector == "other").Text ?? "";
                Append(output, chosen, language, valueOf, Display(value));
            }
            else
            {
                var value = valueOf(body.Trim());
                output.Append(value is null ? "{" + body + "}" : Display(value));
            }
            i = end + 1;
        }
    }

    /// <summary>Numbers use Western digits with comma grouping in both languages, as the web
    /// client formats them for ar-AE and en-AE.</summary>
    private static string Display(object? value) => value switch
    {
        null => "",
        string s => s,
        int or long or decimal or short or byte => ((IFormattable)value).ToString("#,0.##########", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static decimal ToDecimal(object? value) => value switch
    {
        null => 0,
        string s when decimal.TryParse(s.Replace(",", "", StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
        IConvertible c => c.ToDecimal(CultureInfo.InvariantCulture),
        _ => 0,
    };

    private static List<(string Selector, string Text)> Branches(string text)
    {
        var branches = new List<(string, string)>();
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            var start = i;
            while (i < text.Length && text[i] != '{' && !char.IsWhiteSpace(text[i])) i++;
            var selector = text[start..i];
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length || text[i] != '{') break;
            var end = Matching(text, i);
            if (end < 0) break;
            branches.Add((selector, text[(i + 1)..end]));
            i = end + 1;
        }
        return branches;
    }

    private static int Matching(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }
}
