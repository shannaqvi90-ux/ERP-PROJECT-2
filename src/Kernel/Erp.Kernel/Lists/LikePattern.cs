using System.Text;
using System.Text.RegularExpressions;

namespace Erp.Kernel.Lists;

/// <summary>
/// A LIKE pattern for rows held in memory, with PostgreSQL's ILIKE semantics: <c>%</c> any run of
/// characters, <c>_</c> one character, a backslash makes the next character literal, the whole
/// value must match, letter case is ignored (culture-invariant). The pattern becomes a regular
/// expression once, when a query is built; every row is then matched against that one expression.
/// One pattern belongs to one query: it holds nothing but the pattern the request gave.
/// </summary>
public sealed class LikePattern
{
    private readonly Regex _regex;

    public LikePattern(string pattern)
    {
        Pattern = pattern;
        _regex = new Regex(ToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    /// <summary>The LIKE pattern as given.</summary>
    public string Pattern { get; }

    /// <summary>Whether the value matches (a missing value never does).</summary>
    public bool IsMatch(string? value) => value is not null && _regex.IsMatch(value);

    /// <summary>The regular expression a LIKE pattern stands for, anchored at both ends.</summary>
    public static string ToRegex(string pattern)
    {
        var regex = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                regex.Append(Regex.Escape(pattern[++i].ToString()));
            }
            else if (c == '%')
            {
                regex.Append(".*");
            }
            else if (c == '_')
            {
                regex.Append('.');
            }
            else
            {
                regex.Append(Regex.Escape(c.ToString()));
            }
        }
        return regex.Append('$').ToString();
    }

    public override string ToString() => Pattern;
}
