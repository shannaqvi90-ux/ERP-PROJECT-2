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
