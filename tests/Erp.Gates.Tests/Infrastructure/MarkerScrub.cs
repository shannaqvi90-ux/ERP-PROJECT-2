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
        // Values that are an id go first and whole; the ids left in the text are then set aside while
        // the other values are removed, and put back.
        foreach (var value in ordered.Where(v => Guid.TryParse(v, out _)))
        {
            text = replace(text, value);
        }
        var kept = new List<string>();
        text = Id().Replace(text, m =>
        {
            kept.Add(m.Value);
            return $"\u0001{kept.Count - 1}\u0002";
        });
        foreach (var value in ordered.Where(v => !Guid.TryParse(v, out _)))
        {
            text = replace(text, value);
        }
        return kept.Count == 0 ? text : Regex.Replace(text, "\u0001(\\d+)\u0002", m => kept[int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)]);
    }
}
