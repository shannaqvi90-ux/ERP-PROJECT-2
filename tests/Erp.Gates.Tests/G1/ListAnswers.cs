using System.Globalization;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1 for what a list answer adds up to. A leak need not carry the other tenant's ids or text: a
/// list engine that remembers totals, group counts or sums (critic p05 round 1, plant L3: a count
/// cache keyed by search and filter) hands tenant B's numbers to tenant A while every row stays
/// tenant A's. So, for every registered list, the other tenant first sends exactly the same
/// request (search words, B's own values, one-letter searches, every filter value of every choice
/// and flag column, every grouping), then the judged tenant sends it, and its answer must agree
/// with its own rows: the total equals the number of rows an exhaustive keyset walk of the same
/// query returns, every walked row is the judged tenant's own (by id, from the database), each
/// group's count and totals equal those of the walked rows with that value, and the counts add
/// up to the total. Both directions run (B then A, judging A; A then B, judging B). The check is
/// blind unless some queries have different true answers in the two tenants, so every list must
/// have at least one query whose true totals differ.
/// </summary>
public static class ListAnswers
{
    public sealed record Result(IReadOnlyList<string> Wrong, int Queries, int Discriminating, IReadOnlyList<string> Blind, int RowsWalked);

    private const int WalkTake = 199;
    private const int MaxPages = 400;

    /// <param name="first">The tenant that sends each query first (fills any cache).</param>
    /// <param name="judged">The tenant whose answer is judged.</param>
    /// <param name="judgedIds">Every id of the judged tenant's rows (from the database).</param>
    public static async Task<Result> RunAsync(ModuleCatalog catalog, HttpClient first, HttpClient judged, IReadOnlySet<Guid> judgedIds,
        IReadOnlySet<Guid> firstIds, IReadOnlyList<string> victimStrings, string label)
    {
        var wrong = new List<string>();
        var blind = new List<string>();
        var queries = 0;
        var discriminating = 0;
        var walked = 0;
        foreach (var binding in catalog.ListBindings)
        {
            var list = binding.Definition;
            var listDiscriminating = 0;
            foreach (var query in QueriesFor(list, victimStrings))
            {
                var uri = $"{list.Endpoint}?take=50{query}";
                var firstAnswer = await GetAsync(first, uri);
                var answer = await GetAsync(judged, uri);
                if (firstAnswer is null || answer is null)
                {
                    wrong.Add($"{label}: GET {uri} was not answered with 200 (first {(firstAnswer is null ? "refused" : "ok")}, judged {(answer is null ? "refused" : "ok")})");
                    continue;
                }
                queries++;
                var rows = await WalkAsync(judged, list.Endpoint, WithoutGrouping(query));
                if (rows is null)
                {
                    wrong.Add($"{label}: keyset walk of GET {list.Endpoint}?{WithoutGrouping(query).TrimStart('&')} did not finish");
                    continue;
                }
                walked += rows.Count;
                var firstRows = await WalkAsync(first, list.Endpoint, WithoutGrouping(query));
                var total = answer.Value.GetProperty("total").GetInt32();
                if (total != rows.Count)
                {
                    wrong.Add($"{label}: GET {uri} answered total {total}, but walking the same query returns {rows.Count} rows " +
                              $"(the other tenant's answer to the same request was {firstAnswer.Value.GetProperty("total").GetInt32()})");
                }
                var ids = rows.Select(r => r.GetProperty("id").GetGuid()).ToList();
                if (ids.Distinct().Count() != ids.Count)
                {
                    wrong.Add($"{label}: walking {list.Endpoint}{query} returned a row twice");
                }
                foreach (var id in ids.Where(i => !judgedIds.Contains(i)).Take(3))
                {
                    wrong.Add($"{label}: walking {list.Endpoint}{query} returned row {id}, which is not the judged tenant's" +
                              (firstIds.Contains(id) ? " (it is the other tenant's)" : ""));
                }
                if (firstRows is not null && firstRows.Count != rows.Count)
                {
                    discriminating++;
                    listDiscriminating++;
                }
                if (answer.Value.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
                {
                    var column = GroupColumn(query)!;
                    wrong.AddRange(JudgeGroups(list, column, groups, rows, total).Select(p => $"{label}: GET {uri}: {p}"));
                }
                else if (GroupColumn(query) is { } missing)
                {
                    wrong.Add($"{label}: GET {uri} grouped by {missing} but the page carries no groups");
                }
            }
            if (listDiscriminating == 0)
            {
                blind.Add($"{label}: list '{list.Key}': no query had different true answers in the two tenants, so a shared total or group count would go unseen");
            }
        }
        return new Result(wrong, queries, discriminating, blind, walked);
    }

    /// <summary>Query strings (each starting with '&amp;') for a list: everything, one-letter
    /// searches, the other tenant's own words, every value of every choice and flag column, text
    /// filters with the other tenant's values, and every grouping alone and with a search.</summary>
    public static IEnumerable<string> QueriesFor(ListDefinition list, IReadOnlyList<string> victimStrings)
    {
        var searchable = list.SearchFields.Count > 0;
        var words = victimStrings
            .Select(s => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "")
            .Where(w => w.Length is >= 2 and <= 40)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        var queries = new List<string> { "" };
        if (searchable)
        {
            queries.AddRange(new[] { "a", "e", "o", "s", "n" }.Select(l => "&search=" + l));
            queries.AddRange(words.Select(w => "&search=" + Uri.EscapeDataString(w)));
        }
        foreach (var column in list.Columns.Where(c => c.Filterable))
        {
            switch (column.Type)
            {
                case ListColumnType.Choice:
                    queries.AddRange((column.Choices ?? []).Select(c => Filter(ListFilterText.Eq(column.Key, c.Value))));
                    break;
                case ListColumnType.Boolean:
                    queries.Add(Filter(ListFilterText.Eq(column.Key, true)));
                    queries.Add(Filter(ListFilterText.Eq(column.Key, false)));
                    break;
                case ListColumnType.Text:
                    queries.AddRange(words.Take(4).Select(w => Filter($"{column.Key} contains {ListFilterText.Quote(w)}")));
                    queries.Add(Filter($"{column.Key} contains 'a'"));
                    break;
            }
            queries.Add(Filter($"{column.Key} is null"));
        }
        foreach (var column in list.Columns.Where(c => c.Groupable))
        {
            queries.Add("&groupBy=" + column.Key);
            if (searchable)
            {
                queries.Add("&search=a&groupBy=" + column.Key);
                queries.AddRange(words.Take(3).Select(w => $"&search={Uri.EscapeDataString(w)}&groupBy={column.Key}"));
            }
        }
        return queries.Distinct(StringComparer.Ordinal);
    }

    private static string Filter(string expression) => "&filter=" + Uri.EscapeDataString(expression);

    private static string? GroupColumn(string query)
    {
        var at = query.IndexOf("&groupBy=", StringComparison.Ordinal);
        return at < 0 ? null : query[(at + "&groupBy=".Length)..];
    }

    private static string WithoutGrouping(string query)
    {
        var at = query.IndexOf("&groupBy=", StringComparison.Ordinal);
        return at < 0 ? query : query[..at];
    }

    private static IEnumerable<string> JudgeGroups(ListDefinition list, string column, JsonElement groups, IReadOnlyList<JsonElement> rows, int total)
    {
        var counted = groups.EnumerateArray().Sum(g => g.GetProperty("count").GetInt32());
        if (counted != total)
        {
            yield return $"group counts add up to {counted}, the total is {total}";
        }
        var expected = rows.GroupBy(r => Raw(r, column)).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var aggregates = list.Columns.Where(c => c.Aggregate).Select(c => c.Key).ToList();
        foreach (var group in groups.EnumerateArray())
        {
            var key = group.TryGetProperty("key", out var k) ? k.GetRawText() : "null";
            seen.Add(key);
            var count = group.GetProperty("count").GetInt32();
            var mine = expected.GetValueOrDefault(key) ?? [];
            if (count != mine.Count)
            {
                yield return $"group {key} counts {count}, but {mine.Count} of the walked rows have that {column}";
            }
            if (aggregates.Count > 0 && group.TryGetProperty("totals", out var totals) && totals.ValueKind == JsonValueKind.Object)
            {
                foreach (var aggregate in aggregates)
                {
                    if (!totals.TryGetProperty(aggregate, out var stated))
                    {
                        continue;
                    }
                    var sum = mine.Sum(r => Number(r, aggregate));
                    if (Number(stated) != sum)
                    {
                        yield return $"group {key} totals {aggregate} = {stated.GetRawText()}, but the walked rows add up to {sum.ToString(CultureInfo.InvariantCulture)}";
                    }
                }
            }
        }
        foreach (var missing in expected.Keys.Where(k => !seen.Contains(k)))
        {
            yield return $"no group for {column} = {missing}, which {expected[missing].Count} walked rows have";
        }
    }

    private static string Raw(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) ? value.GetRawText() : "null";

    private static decimal Number(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) ? Number(value) : 0m;

    private static decimal Number(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDecimal(),
        JsonValueKind.String => decimal.Parse(value.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture),
        _ => 0m,
    };

    private static async Task<JsonElement?> GetAsync(HttpClient client, string uri)
    {
        using var response = await client.GetAsync(uri);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// <summary>Every row of the query, page by page with the keyset cursor.</summary>
    private static async Task<IReadOnlyList<JsonElement>?> WalkAsync(HttpClient client, string endpoint, string query)
    {
        var rows = new List<JsonElement>();
        string? next = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var uri = $"{endpoint}?take={WalkTake}{query}" + (next is null ? "" : "&after=" + Uri.EscapeDataString(next));
            if (await GetAsync(client, uri) is not { } answer)
            {
                return null;
            }
            rows.AddRange(answer.GetProperty("items").EnumerateArray());
            next = answer.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            if (next is null)
            {
                return rows;
            }
        }
        return null;
    }
}
