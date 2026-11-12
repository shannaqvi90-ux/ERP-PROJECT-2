using System.Text.RegularExpressions;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// Removes the values a request sent (and the texts the attacker stored in its own records) from an
/// answer before it is searched for another company's markers, without cutting a marker that is an
/// id out of the answer. A stored text of three or more letters and digits that are all hex digits
/// ("123", "dad", "20002") also occurs inside a random id now and then; replacing it there broke
/// the id, and the planted e-mail lookup (which answers tenant B's id) went unnoticed in one run
/// in a number (GateSelfTests, round 19). An id is replaced only as a whole: when a value is that id.
/// </summary>
public static partial class MarkerScrub
{
    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex Id();

    /// <param name="replace">Replaces one value in a text (each caller's own escaping rules).</param>
    public static string Scrub(string text, IEnumerable<string> values, Func<string, string, string> replace)
    {
        var ordered = values.Where(v => v.Length > 0).Distinct().OrderByDescending(v => v.Length).ToList();
        // Values that hold an id (an id itself, a text with one in it) go first, on the whole text.
        foreach (var value in ordered.Where(v => Id().IsMatch(v)))
        {
            text = replace(text, value);
        }
        // The other values are removed between the ids that are left, never inside one.
        var rest = ordered.Where(v => !Id().IsMatch(v)).ToList();
        if (rest.Count == 0)
        {
            return text;
        }
        var result = new System.Text.StringBuilder(text.Length);
        var from = 0;
        foreach (Match id in Id().Matches(text))
        {
            result.Append(Cut(text[from..id.Index], rest, replace)).Append(id.Value);
            from = id.Index + id.Length;
        }
        return result.Append(Cut(text[from..], rest, replace)).ToString();
    }

    private static string Cut(string segment, List<string> values, Func<string, string, string> replace)
    {
        foreach (var value in values)
        {
            segment = replace(segment, value);
        }
        return segment;
    }
}
