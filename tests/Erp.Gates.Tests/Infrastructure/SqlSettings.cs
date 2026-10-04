using System.Text;
using System.Text.RegularExpressions;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// Reads what a SQL text does to session settings: every <c>set_config(name, value, is_local)</c>
/// call and every <c>SET [LOCAL|SESSION] name {=|TO} value</c>, with the value resolved from the
/// statement's parameters. The isolation gate uses it to judge the tenant a request's SQL actually
/// runs under: the value a statement gives <c>app.tenant_id</c>, not which class sent it.
/// </summary>
public static partial class SqlSettings
{
    /// <summary>One setting change.</summary>
    /// <param name="Name">The setting's name, or null when the statement computes it (the gate cannot read it).</param>
    /// <param name="Value">The value it sets, or null when computed (see <paramref name="Computed"/>) or SQL NULL.</param>
    /// <param name="Computed">True when the value is an expression rather than a literal or a parameter.</param>
    /// <param name="Local">True for a transaction-local change; false for the whole connection; null when computed.</param>
    public sealed record Change(string? Name, string? Value, bool Computed, bool? Local, string Source);

    /// <summary>The tenant setting row-level security reads.</summary>
    public const string TenantSetting = "app.tenant_id";

    public static IReadOnlyList<Change> Parse(string text, IReadOnlyDictionary<string, string?> parameters)
    {
        // Comments go, and quoted identifiers are read as the names they quote ("set_config",
        // "app"."tenant_id"), so neither hides a setting change from the rules below.
        text = Normalize(text);
        var changes = new List<Change>();
        foreach (Match call in SetConfigCall().Matches(text))
        {
            var open = call.Index + call.Length - 1;
            var args = Arguments(text, open, out var end);
            var source = text[call.Index..Math.Min(text.Length, end + 1)];
            if (args.Count != 3)
            {
                changes.Add(new Change(null, null, true, null, source));
                continue;
            }
            var (name, nameComputed) = Evaluate(args[0], parameters);
            var (value, valueComputed) = Evaluate(args[1], parameters);
            var (local, localComputed) = Evaluate(args[2], parameters);
            bool? isLocal = localComputed ? null : local is { } l && (l.Equals("true", StringComparison.OrdinalIgnoreCase) || l == "t" || l == "on" || l == "1");
            changes.Add(new Change(nameComputed ? null : name, valueComputed ? null : value, valueComputed, isLocal, source));
        }
        var read = new HashSet<int>();
        foreach (Match set in SetStatement().Matches(text))
        {
            read.Add(set.Index);
            var scope = set.Groups["scope"].Value;
            var (value, computed) = Evaluate(set.Groups["value"].Value, parameters);
            changes.Add(new Change(set.Groups["name"].Value, computed ? null : value, computed,
                scope.Equals("LOCAL", StringComparison.OrdinalIgnoreCase), set.Value.Trim().TrimStart(';').Trim()));
        }
        // Every other SET or RESET of a dotted (custom) setting: RESET clears it for the whole
        // connection; a SET the reader above could not take apart is refused, not trusted.
        // Built-in settings without a dot (SET TRANSACTION READ ONLY, SET LOCAL ROLE) are the
        // settings rule's business (which code and which database user), not this one's.
        foreach (Match other in SetOrReset().Matches(text))
        {
            if (read.Contains(other.Index) || !other.Groups["name"].Value.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }
            var source = other.Value.Trim().TrimStart(';').Trim();
            changes.Add(other.Groups["verb"].Value.Equals("RESET", StringComparison.OrdinalIgnoreCase)
                ? new Change(other.Groups["name"].Value.Replace(" ", "", StringComparison.Ordinal), "", false, false, source)
                : new Change(null, null, true, null, source));
        }
        return changes;
    }

    /// <summary>The SQL text without comments, with each double-quoted identifier written as the
    /// name it quotes (lower case is kept as written; PostgreSQL compares quoted names exactly,
    /// and a setting name with upper case letters is a different setting, which the rules then
    /// do not treat as the tenant). String literals are kept as they are.</summary>
    public static string Normalize(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\'')
            {
                var close = i + 1;
                while (close < text.Length && !(text[close] == '\'' && (close + 1 >= text.Length || text[close + 1] != '\'')))
                {
                    close += text[close] == '\'' ? 2 : 1;
                }
                result.Append(text, i, Math.Min(close, text.Length - 1) - i + 1);
                i = close;
            }
            else if (c == '"')
            {
                var close = text.IndexOf('"', i + 1);
                if (close < 0)
                {
                    result.Append(text, i, text.Length - i);
                    break;
                }
                result.Append(text, i + 1, close - i - 1);
                i = close;
            }
            else if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                var newline = text.IndexOf('\n', i);
                result.Append(' ');
                i = newline < 0 ? text.Length : newline - 1;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                result.Append(' ');
                i = close < 0 ? text.Length : close + 1;
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }

    /// <summary>The top-level, comma-separated arguments of the call whose '(' is at <paramref name="open"/>.</summary>
    private static List<string> Arguments(string text, int open, out int end)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        end = text.Length - 1;
        for (var i = open + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\'' || c == '"')
            {
                var close = i + 1;
                while (close < text.Length)
                {
                    if (text[close] == c)
                    {
                        if (close + 1 < text.Length && text[close + 1] == c)
                        {
                            close += 2;
                            continue;
                        }
                        break;
                    }
                    close++;
                }
                current.Append(text, i, Math.Min(close, text.Length - 1) - i + 1);
                i = close;
                continue;
            }
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                if (depth == 0)
                {
                    args.Add(current.ToString().Trim());
                    end = i;
                    return args;
                }
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                args.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        args.Add(current.ToString().Trim());
        return args;
    }

    /// <summary>A literal, a parameter reference or NULL (each with optional casts); anything else is computed.</summary>
    private static (string? Value, bool Computed) Evaluate(string expression, IReadOnlyDictionary<string, string?> parameters)
    {
        var e = Cast().Replace(expression.Trim(), "").Trim();
        while (e.Length > 1 && e[0] == '(' && e[^1] == ')')
        {
            e = e[1..^1].Trim();
        }
        if (Literal().IsMatch(e))
        {
            return (e[1..^1].Replace("''", "'", StringComparison.Ordinal), false);
        }
        if (e.Length >= 3 && (e.StartsWith("E'", StringComparison.OrdinalIgnoreCase)) && e[^1] == '\'')
        {
            return (null, true);
        }
        if (e.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            return (null, false);
        }
        if (e.Equals("true", StringComparison.OrdinalIgnoreCase) || e.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return (e.ToLowerInvariant(), false);
        }
        if (Parameter().Match(e) is { Success: true } p)
        {
            var key = p.Groups["positional"].Success ? "$" + p.Groups["positional"].Value : p.Groups["name"].Value;
            return parameters.TryGetValue(key, out var value) ? (value, false) : (null, true);
        }
        if (Identifier().IsMatch(e) && !e.Contains('(', StringComparison.Ordinal))
        {
            // A bare word in SET name TO word (SET x TO DEFAULT, SET x = on).
            return (e, false);
        }
        return (null, true);
    }

    [GeneratedRegex(@"^'(?:[^']|'')*'$", RegexOptions.CultureInvariant)]
    private static partial Regex Literal();

    [GeneratedRegex(@"\bset_config\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SetConfigCall();

    [GeneratedRegex(@"(?:^|;)\s*(?<verb>SET|RESET)\s+(?:(?:LOCAL|SESSION)\s+)?(?<name>[a-z_][a-z0-9_$]*(?:\s*\.\s*[a-z0-9_$]+)*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SetOrReset();

    [GeneratedRegex(@"(?:^|;)\s*SET\s+(?:(?<scope>LOCAL|SESSION)\s+)?(?<name>[a-z_][a-z0-9_]*\.[a-z0-9_.]+)\s*(?:=|\bTO\b)\s*(?<value>'(?:[^']|'')*'|[^;\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SetStatement();

    [GeneratedRegex(@"::\s*[a-z_][a-z0-9_ ]*(?:\[\])?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Cast();

    [GeneratedRegex(@"^(?:[@:](?<name>[a-z_][a-z0-9_]*)|\$(?<positional>[0-9]+))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Parameter();

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
