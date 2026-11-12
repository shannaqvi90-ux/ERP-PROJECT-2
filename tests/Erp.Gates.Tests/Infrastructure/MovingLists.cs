using System.Text.Json;
using System.Text.Json.Nodes;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// Tells a list that moved between the asks of a pair from a list that answers differently for a
/// value of another company.
///
/// The differential asks the same read twice, once with a value of company Y and once with a value
/// that exists nowhere (the control), in the order answer, control, answer, control. An audit log or
/// a job list gains rows while the pair runs, so two asks of it can differ although nothing tells
/// the two values apart (a 200-row window slides by one row). A list that moves changes with
/// time only: every row that differs between the answers is present in the four answers, in time order,
/// as a block, either absent and then present or present and then absent (one change in the whole
/// sequence), and a number outside the lists (a total) only grows or only shrinks. A deterministic oracle
/// alternates with the side: a row shown to the value of Y alone is present, absent, present,
/// absent; a row hidden from it, absent, present, absent, present. So the pair is explained by
/// movement only if every differing item changes presence at most once in time order, and the
/// rows that all four answers hold keep one order. Anything else (another status, a text that is
/// not JSON, an item that flips back and forth, a reordering) is still an oracle.
/// </summary>
public static class MovingLists
{
    /// <summary>Whether the four answers, in the order they were asked (answer, control, answer, control), differ by movement alone.</summary>
    public static bool ExplainedByMovement(string answer1, string control1, string answer2, string control2)
    {
        var samples = new[] { answer1, control1, answer2, control2 };
        var parsed = new List<Sample>();
        foreach (var text in samples)
        {
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                return false;
            }
            if (root is null) return false;
            var sample = new Sample();
            Collect(root, "", sample);
            parsed.Add(sample);
        }

        // A number outside the lists (a total, a count) grows or shrinks with time; it never goes up and down.
        foreach (var path in parsed.SelectMany(p => p.Scalars.Keys).Distinct(StringComparer.Ordinal))
        {
            var series = parsed.Select(p => p.Scalars.GetValueOrDefault(path)).ToArray();
            if (series.All(v => v is not null && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
            {
                var numbers = series.Select(v => double.Parse(v!, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var up = numbers.Zip(numbers.Skip(1), (a, b) => b >= a).All(x => x);
                var down = numbers.Zip(numbers.Skip(1), (a, b) => b <= a).All(x => x);
                if (!up && !down) return false;
                continue;
            }
            var changed = 0;
            for (var i = 1; i < series.Length; i++)
            {
                if (series[i] != series[i - 1]) changed++;
            }
            if (changed > 1) return false;
        }

        // Presence of each row in the four answers, in time order.
        var all = parsed.SelectMany(p => p.Items.Keys).Distinct(StringComparer.Ordinal);
        foreach (var item in all)
        {
            var presence = parsed.Select(p => p.Items.ContainsKey(item)).ToArray();
            var changes = 0;
            for (var i = 1; i < presence.Length; i++)
            {
                if (presence[i] != presence[i - 1]) changes++;
            }
            if (changes > 1) return false;
        }

        // The rows every answer holds keep one order in each list.
        var lists = parsed.SelectMany(p => p.Order.Keys).Distinct(StringComparer.Ordinal);
        foreach (var list in lists)
        {
            var orders = parsed.Select(p => p.Order.TryGetValue(list, out var rows) ? rows : []).ToList();
            var common = orders.Select(o => o.ToHashSet(StringComparer.Ordinal)).Aggregate((a, b) => { a.IntersectWith(b); return a; });
            var reference = orders[0].Where(common.Contains).ToList();
            if (orders.Any(o => !o.Where(common.Contains).SequenceEqual(reference, StringComparer.Ordinal))) return false;
        }
        return true;
    }

    /// <summary>Query parameters that choose a window of a list, not which rows it holds.</summary>
    private static readonly HashSet<string> PagingNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "take", "skip", "limit", "offset", "top", "size", "page", "pageSize", "page_size", "perPage", "per_page", "count", "first",
    };

    /// <summary>Whether the request (<paramref name="label"/> holds its path and query) sends
    /// <paramref name="value"/> as a paging parameter: a window of the list, which on a list that is
    /// longer than the window answers other rows for another number, so two numbers are not compared
    /// row by row (the rows are judged for company Y's markers all the same).</summary>
    public static bool SendsAsPagingValue(string label, string value)
    {
        var at = label.IndexOf('?', StringComparison.Ordinal);
        if (at < 0) return false;
        foreach (var pair in label[(at + 1)..].Split('&'))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) continue;
            if (PagingNames.Contains(Uri.UnescapeDataString(pair[..eq])) && Uri.UnescapeDataString(pair[(eq + 1)..]) == value) return true;
        }
        return false;
    }

    /// <summary>
    /// The four answers (answer, control, answer, control, in time order) of a paging parameter differ
    /// by the window and the movement of the list alone: every answer has the same shape (the same
    /// properties; a list may be empty in one), and a number outside the lists (a total) only grows
    /// or only shrinks over the four. Rows are not compared, so a number that selects other rows is not
    /// an oracle, but a number that changes a total with the side (a count of 1 for the value of Y,
    /// 0 for the control, 1, 0), another status, a property that appears for one side, or a text that
    /// is not JSON still is.
    /// </summary>
    public static bool PagingExplainedByMovement(string answer1, string control1, string answer2, string control2)
    {
        var parsed = new List<JsonNode>();
        foreach (var text in new[] { answer1, control1, answer2, control2 })
        {
            try
            {
                if (JsonNode.Parse(text) is not { } root) return false;
                parsed.Add(root);
            }
            catch (JsonException)
            {
                return false;
            }
        }
        for (var i = 1; i < parsed.Count; i++)
        {
            if (!ShapesAgree(parsed[0], parsed[i])) return false;
        }
        var samples = parsed.Select(root =>
        {
            var sample = new Sample();
            Collect(root, "", sample);
            return sample;
        }).ToList();
        foreach (var path in samples.SelectMany(p => p.Scalars.Keys).Distinct(StringComparer.Ordinal))
        {
            var series = samples.Select(p => p.Scalars.GetValueOrDefault(path)).ToArray();
            if (!series.All(v => v is not null && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
            {
                continue;
            }
            var numbers = series.Select(v => double.Parse(v!, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var up = numbers.Zip(numbers.Skip(1), (a, b) => b >= a).All(x => x);
            var down = numbers.Zip(numbers.Skip(1), (a, b) => b <= a).All(x => x);
            if (!up && !down) return false;
        }
        return true;
    }

    private static bool ShapesAgree(JsonNode? a, JsonNode? b)
    {
        switch (a, b)
        {
            case (JsonObject x, JsonObject y):
                return x.Select(kv => kv.Key).Order(StringComparer.Ordinal).SequenceEqual(y.Select(kv => kv.Key).Order(StringComparer.Ordinal), StringComparer.Ordinal)
                    && x.All(kv => ShapesAgree(kv.Value, y[kv.Key]));
            case (JsonArray x, JsonArray y):
                return x.Count == 0 || y.Count == 0 || ShapesAgree(x[0], y[0]);
            case (JsonObject, _) or (_, JsonObject) or (JsonArray, _) or (_, JsonArray):
                return (a is null || b is null) && !(a is JsonObject or JsonArray && b is JsonObject or JsonArray);
            default:
                return true;
        }
    }

    private sealed class Sample
    {
        /// <summary>Every list row, by the list's path and the row's text (and its occurrence).</summary>
        public Dictionary<string, bool> Items { get; } = new(StringComparer.Ordinal);

        /// <summary>Every scalar outside the lists by its path: its JSON text.</summary>
        public Dictionary<string, string> Scalars { get; } = new(StringComparer.Ordinal);

        /// <summary>The rows of each list, in order.</summary>
        public Dictionary<string, List<string>> Order { get; } = new(StringComparer.Ordinal);
    }

    private static void Collect(JsonNode node, string path, Sample into)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, child) in o)
                {
                    if (child is null) into.Scalars[path + "/" + key] = "null";
                    else Collect(child, path + "/" + key, into);
                }
                break;
            case JsonArray a:
                var rows = new List<string>();
                var seen = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var child in a)
                {
                    var text = child?.ToJsonString() ?? "null";
                    seen[text] = seen.GetValueOrDefault(text) + 1;
                    var row = text + "#" + seen[text];
                    rows.Add(row);
                    into.Items["r|" + path + "|" + row] = true;
                }
                into.Order[path] = rows;
                break;
            default:
                into.Scalars[path] = node.ToJsonString();
                break;
        }
    }
}
