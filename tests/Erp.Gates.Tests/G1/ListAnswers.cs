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
/// up to the total. The two tenants walk each query in lock step over several small keyset pages
/// (one tenant's page n, then the other's), and the total and groups of every page of both walks
/// are judged (critic p05 round 4, plant L6: pages after the first reused the total the last
/// first page counted, whatever its tenant). Both directions run (B then A, judging A; A then B,
/// judging B). Every query is also walked by offset (<c>skip</c>, the paging the client uses when a
/// user jumps through a long list), in the same lock step, and the total, the groups and the rows of
/// every offset page are judged the same way (critic p05 round 5, plant L10: a pooled per-query
/// scratch object, never cleared and keyed without the tenant, handed offset pages the total the
/// last first page counted, whatever its tenant; every first page and every keyset page stayed
/// right). The check is blind unless some queries have different true answers in the two tenants
/// and some are answered over more than one keyset page and more than one offset page, so every
/// list must have all three. Every query is judged a second time with an explicit sort, and every
/// sort key (each sortable column, both directions) meets every kind of query: alone, with a
/// search, a filter, a grouping and a grouping with a search (critic p05 round 7, plant L11: a
/// total memo keyed by search, filter and sort with no tenant, served only to requests carrying both
/// a sort and a search, which is what the client sends after a header click). A sorted walk must
/// return exactly the rows of the same query unsorted, and a list with sortable columns must have
/// sorted queries, sorted searches among them, with different true answers in the two tenants.
/// </summary>
public static class ListAnswers
{
    /// <param name="PagesJudged">Pages of keyset walks whose total (and groups) were judged.</param>
    /// <param name="OffsetPagesJudged">Pages of offset (skip) walks whose total, groups and rows were judged.</param>
    /// <param name="SortedQueries">Queries judged with an explicit sort (of <paramref name="Queries"/>).</param>
    /// <param name="SortedDiscriminating">Sorted queries with different true answers in the two tenants.</param>
    public sealed record Result(IReadOnlyList<string> Wrong, int Queries, int Discriminating, IReadOnlyList<string> Blind, int RowsWalked, int PagesJudged = 0,
        int OffsetPagesJudged = 0, int SortedQueries = 0, int SortedDiscriminating = 0);

    private const int MaxTake = 199;
    private const int MaxPages = 400;

    /// <summary>Pages the walk aims for: enough that every query with a few rows is answered over
    /// several keyset pages (critic p05 round 4, plant L6: the pages after the first reused the
    /// total the last first page counted, whatever its tenant, and a walk of one page never saw it).</summary>
    private const int PagesAimedFor = 4;

    /// <param name="first">The tenant that sends each query first (fills any cache).</param>
    /// <param name="judged">The tenant whose answer is judged.</param>
    /// <param name="judgedIds">Every id of the judged tenant's rows (from the database).</param>
    public static async Task<Result> RunAsync(ModuleCatalog catalog, HttpClient first, HttpClient judged, IReadOnlySet<Guid> judgedIds,
        IReadOnlySet<Guid> firstIds, IReadOnlyList<string> victimStrings, string label, Func<ListDefinition, bool>? sortedLists = null)
    {
        var tally = new Tally(label, judgedIds, firstIds);
        foreach (var binding in catalog.ListBindings)
        {
            var list = binding.Definition;
            var listDiscriminating = 0;
            var listPaged = 0;
            var listOffsetPaged = 0;
            var baseQueries = QueriesFor(list, victimStrings).ToList();
            var unsorted = new Dictionary<string, Judged>(StringComparer.Ordinal);
            foreach (var query in baseQueries)
            {
                if (await JudgeAsync(tally, list, first, judged, query) is not { } outcome)
                {
                    continue;
                }
                unsorted[query] = outcome;
                listDiscriminating += outcome.Discriminating ? 1 : 0;
                listPaged += outcome.Paged ? 1 : 0;
                listOffsetPaged += outcome.OffsetPaged ? 1 : 0;
            }
            // The same queries again with an explicit sort (critic p05 round 7, plant L11: a total
            // memo keyed by search, filter and sort, served only when a request has both a sort and
            // a search, which is what the client sends once a user clicks a header). Every query is
            // sent with a sort, and every sortable column in both directions is sent alone, with a
            // search, with a filter, with a grouping and with a grouping and a search, so a memo
            // that only one sort key, one kind of query or their combination reaches is judged.
            var sortedDiscriminating = 0;
            var sortedSearchDiscriminating = 0;
            var sortedPaged = 0;
            var sortedOffsetPaged = 0;
            var keysJudged = new HashSet<string>(StringComparer.Ordinal);
            var judgeSorted = sortedLists?.Invoke(list) ?? true;
            foreach (var (query, basis, sort) in judgeSorted ? SortedQueriesFor(list, baseQueries) : [])
            {
                if (await JudgeAsync(tally, list, first, judged, query) is not { } outcome)
                {
                    continue;
                }
                tally.SortedQueries++;
                keysJudged.Add(sort);
                if (outcome.Discriminating)
                {
                    tally.SortedDiscriminating++;
                    sortedDiscriminating++;
                    sortedSearchDiscriminating += query.Contains("&search=", StringComparison.Ordinal) ? 1 : 0;
                }
                sortedPaged += outcome.Paged ? 1 : 0;
                sortedOffsetPaged += outcome.OffsetPaged ? 1 : 0;
                // A sort orders the rows a query matches; it never changes which rows those are.
                if (unsorted.TryGetValue(basis, out var plain))
                {
                    foreach (var (who, sorted, own) in new[] { ("judged", outcome.JudgedRows, plain.JudgedRows), ("other tenant's", outcome.FirstRows, plain.FirstRows) })
                    {
                        if (!sorted.SetEquals(own))
                        {
                            tally.Wrong.Add($"{label}: the {who} walk of GET {list.Endpoint}?{query.TrimStart('&')} returned {sorted.Count} rows, " +
                                            $"the same query without the sort {own.Count} ({sorted.Except(own).Count()} rows only when sorted, {own.Except(sorted).Count()} only unsorted)");
                        }
                    }
                }
            }
            if (listDiscriminating == 0)
            {
                tally.Blind.Add($"{label}: list '{list.Key}': no query had different true answers in the two tenants, so a shared total or group count would go unseen");
            }
            if (listPaged == 0)
            {
                tally.Blind.Add($"{label}: list '{list.Key}': no query was answered over more than one keyset page, so a total or group remembered for the next pages would go unseen");
            }
            if (listOffsetPaged == 0)
            {
                tally.Blind.Add($"{label}: list '{list.Key}': no query was answered over more than one offset (skip) page, so a total or group remembered for a jump into the list would go unseen");
            }
            var sortKeys = SortKeys(list);
            if (judgeSorted && sortKeys.Count > 0)
            {
                foreach (var missing in sortKeys.Where(k => !keysJudged.Contains(k)))
                {
                    tally.Blind.Add($"{label}: list '{list.Key}': no query sorted by '{missing}' was answered, so a total or group remembered for that sort would go unseen");
                }
                if (sortedDiscriminating == 0)
                {
                    tally.Blind.Add($"{label}: list '{list.Key}': no sorted query had different true answers in the two tenants, so a total remembered for a sorted query would go unseen");
                }
                if (list.SearchFields.Count > 0 && sortedSearchDiscriminating == 0)
                {
                    tally.Blind.Add($"{label}: list '{list.Key}': no sorted search had different true answers in the two tenants, so a total remembered for a sorted search would go unseen");
                }
                if (sortedPaged == 0 || sortedOffsetPaged == 0)
                {
                    tally.Blind.Add($"{label}: list '{list.Key}': no sorted query was answered over more than one keyset page and more than one offset page, so a total remembered for a sorted list's later pages would go unseen");
                }
            }
        }
        return new Result(tally.Wrong, tally.Queries, tally.Discriminating, tally.Blind, tally.Walked, tally.PagesJudged, tally.OffsetPagesJudged,
            tally.SortedQueries, tally.SortedDiscriminating);
    }

    /// <summary>What one judged query returned: the ids of both tenants' walks and what made the
    /// query count toward the blind-spot checks.</summary>
    private sealed record Judged(HashSet<Guid> JudgedRows, HashSet<Guid> FirstRows, bool Discriminating, bool Paged, bool OffsetPaged);

    /// <summary>Counts and findings of one run (both tenants, every list).</summary>
    private sealed class Tally(string label, IReadOnlySet<Guid> judgedIds, IReadOnlySet<Guid> firstIds)
    {
        public string Label { get; } = label;
        public IReadOnlySet<Guid> JudgedIds { get; } = judgedIds;
        public IReadOnlySet<Guid> FirstIds { get; } = firstIds;
        public List<string> Wrong { get; } = [];
        public List<string> Blind { get; } = [];
        public int Queries { get; set; }
        public int Discriminating { get; set; }
        public int Walked { get; set; }
        public int PagesJudged { get; set; }
        public int OffsetPagesJudged { get; set; }
        public int SortedQueries { get; set; }
        public int SortedDiscriminating { get; set; }
    }

    /// <summary>One query, judged: the other tenant sends it first, then the judged tenant; both walk
    /// it by keyset and by offset in lock step, and every page's total and groups must be those of
    /// the tenant's own walked rows. Null when it could not be judged (reported as wrong).</summary>
    private static async Task<Judged?> JudgeAsync(Tally tally, ListDefinition list, HttpClient first, HttpClient judged, string query)
    {
        var label = tally.Label;
        var judgedIds = tally.JudgedIds;
        var firstIds = tally.FirstIds;
        var wrong = tally.Wrong;
        var uri = $"{list.Endpoint}?take=50{query}";
        var firstAnswer = await GetAsync(first, uri);
        var answer = await GetAsync(judged, uri);
        if (firstAnswer is null || answer is null)
        {
            wrong.Add($"{label}: GET {uri} was not answered with 200 (first {(firstAnswer is null ? "refused" : "ok")}, judged {(answer is null ? "refused" : "ok")})");
            return null;
        }
        tally.Queries++;
        var total = answer.Value.GetProperty("total").GetInt32();
        var firstTotal = firstAnswer.Value.GetProperty("total").GetInt32();
        // Both tenants walk the same query (grouping kept) in lock step, the judged tenant's
        // page n just before the other tenant's page n: whatever one tenant's request leaves
        // behind is in place when the other's next page is answered. Every page of both walks
        // is judged, not only the first.
        var take = Math.Clamp((Math.Max(total, firstTotal) + PagesAimedFor - 1) / PagesAimedFor, 1, MaxTake);
        var (walk, firstWalk) = await WalkTogetherAsync(judged, first, list.Endpoint, query, take);
        if (walk is null || firstWalk is null)
        {
            wrong.Add($"{label}: keyset walk of GET {list.Endpoint}?take={take}{query} did not finish (judged {(walk is null ? "failed" : "ok")}, other {(firstWalk is null ? "failed" : "ok")})");
            return null;
        }
        var rows = walk.Rows;
        tally.Walked += rows.Count;
        if (total != rows.Count)
        {
            wrong.Add($"{label}: GET {uri} answered total {total}, but walking the same query returns {rows.Count} rows " +
                      $"(the other tenant's answer to the same request was {firstTotal})");
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
        var firstRowIds = firstWalk.Rows.Select(r => r.GetProperty("id").GetGuid()).ToList();
        foreach (var id in firstRowIds.Where(i => !firstIds.Contains(i)).Take(3))
        {
            wrong.Add($"{label}: the other tenant's walk of {list.Endpoint}{query} returned row {id}, which is not its own" +
                      (judgedIds.Contains(id) ? " (it is the judged tenant's)" : ""));
        }
        var discriminating = firstWalk.Rows.Count != rows.Count;
        if (discriminating)
        {
            tally.Discriminating++;
        }
        var paged = walk.Pages.Count > 1;
        var offsetPaged = false;
        var column = GroupColumn(query);
        tally.PagesJudged += walk.Pages.Count + firstWalk.Pages.Count;
        foreach (var (who, pages, mine) in new[] { ("judged", walk.Pages, rows), ("other tenant's", firstWalk.Pages, firstWalk.Rows) })
        {
            for (var n = 0; n < pages.Count; n++)
            {
                var page = pages[n];
                var where = $"{label}: the {who} page {n + 1} of {pages.Count} of GET {list.Endpoint}?take={take}{query}";
                var pageTotal = page.GetProperty("total").GetInt32();
                if (pageTotal != mine.Count)
                {
                    wrong.Add($"{where} answered total {pageTotal}, but the walk returns {mine.Count} rows");
                }
                if (page.TryGetProperty("groups", out var pageGroups) && pageGroups.ValueKind == JsonValueKind.Array && column is not null)
                {
                    wrong.AddRange(JudgeGroups(list, column, pageGroups, mine, mine.Count).Select(p => $"{where}: {p}"));
                }
                else if (column is not null)
                {
                    wrong.Add($"{where} grouped by {column} but the page carries no groups");
                }
            }
        }
        // The same query again by offset (skip), both tenants in the same lock step: the
        // judged tenant's offset page n just before the other tenant's. Each offset page's
        // total and groups must be those of the keyset walk (the tenant's own rows), and the
        // offset pages together must return exactly the walked rows.
        var (jump, firstJump) = await WalkTogetherAsync(judged, first, list.Endpoint, query, take, offset: true);
        if (jump is null || firstJump is null)
        {
            wrong.Add($"{label}: offset walk of GET {list.Endpoint}?take={take}{query} did not finish (judged {(jump is null ? "failed" : "ok")}, other {(firstJump is null ? "failed" : "ok")})");
        }
        else
        {
            offsetPaged = jump.Pages.Count > 1;
            tally.OffsetPagesJudged += jump.Pages.Count + firstJump.Pages.Count;
            foreach (var (who, offsetWalk, mine) in new[] { ("judged", jump, rows), ("other tenant's", firstJump, firstWalk.Rows) })
            {
                var pages = offsetWalk.Pages;
                for (var n = 0; n < pages.Count; n++)
                {
                    var page = pages[n];
                    var where = $"{label}: the {who} offset page {n + 1} of {pages.Count} (skip={n * take}) of GET {list.Endpoint}?take={take}{query}";
                    var pageTotal = page.GetProperty("total").GetInt32();
                    if (pageTotal != mine.Count)
                    {
                        wrong.Add($"{where} answered total {pageTotal}, but the keyset walk returns {mine.Count} rows");
                    }
                    if (page.TryGetProperty("groups", out var pageGroups) && pageGroups.ValueKind == JsonValueKind.Array && column is not null)
                    {
                        wrong.AddRange(JudgeGroups(list, column, pageGroups, mine, mine.Count).Select(p => $"{where}: {p}"));
                    }
                    else if (column is not null)
                    {
                        wrong.Add($"{where} grouped by {column} but the page carries no groups");
                    }
                }
                var keyset = mine.Select(r => r.GetProperty("id").GetGuid()).ToHashSet();
                var jumped = offsetWalk.Rows.Select(r => r.GetProperty("id").GetGuid()).ToList();
                if (jumped.Count != keyset.Count || !keyset.SetEquals(jumped))
                {
                    var foreign = jumped.Where(i => !keyset.Contains(i)).Take(3).ToList();
                    wrong.Add($"{label}: the {who} offset walk of GET {list.Endpoint}?take={take}{query} returned {jumped.Count} rows ({jumped.Distinct().Count()} distinct), " +
                              $"the keyset walk {keyset.Count}" + (foreign.Count == 0 ? "" : $"; rows not in the keyset walk: {string.Join(", ", foreign)}" +
                              (foreign.Any(i => (who == "judged" ? firstIds : judgedIds).Contains(i)) ? " (the other tenant's)" : "")));
                }
            }
        }
        if (answer.Value.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
        {
            wrong.AddRange(JudgeGroups(list, column!, groups, rows, total).Select(p => $"{label}: GET {uri}: {p}"));
        }
        else if (column is { } missing)
        {
            wrong.Add($"{label}: GET {uri} grouped by {missing} but the page carries no groups");
        }
        return new Judged(ids.ToHashSet(), firstRowIds.ToHashSet(), discriminating, paged, offsetPaged);
    }

    /// <summary>Every sort key of a list: each sortable column ascending and descending.</summary>
    public static IReadOnlyList<string> SortKeys(ListDefinition list) =>
        list.Columns.Where(c => c.Sortable).SelectMany(c => new[] { c.Key, "-" + c.Key }).ToList();

    /// <summary>The sorted twins of a list's queries: within each kind of query (everything,
    /// searches, filters, groupings, groupings with a search) every query gets a sort and every
    /// sort key is used, so each key meets each kind of query at least once; then two keys at once,
    /// alone and with a search. Each comes with the unsorted query it sorts and its sort.</summary>
    public static IEnumerable<(string Query, string Basis, string Sort)> SortedQueriesFor(ListDefinition list, IReadOnlyList<string> baseQueries)
    {
        var keys = SortKeys(list);
        if (keys.Count == 0)
        {
            yield break;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var members in baseQueries.GroupBy(Kind, StringComparer.Ordinal).Select(g => g.ToList()))
        {
            var n = Math.Max(members.Count, keys.Count);
            for (var i = 0; i < n; i++)
            {
                var basis = members[i % members.Count];
                var key = keys[i % keys.Count];
                var query = basis + "&sort=" + Uri.EscapeDataString(key);
                if (seen.Add(query))
                {
                    yield return (query, basis, key);
                }
            }
        }
        var columns = list.Columns.Where(c => c.Sortable).Select(c => c.Key).ToList();
        if (columns.Count >= 2)
        {
            var two = $"-{columns[1]},{columns[0]}";
            foreach (var basis in list.SearchFields.Count > 0 ? new[] { "", "&search=a" } : [""])
            {
                if (baseQueries.Contains(basis, StringComparer.Ordinal) && seen.Add(basis + "&sort=" + Uri.EscapeDataString(two)))
                {
                    yield return (basis + "&sort=" + Uri.EscapeDataString(two), basis, two);
                }
            }
        }
    }

    private static string Kind(string query) =>
        query.Contains("&groupBy=", StringComparison.Ordinal)
            ? query.Contains("&search=", StringComparison.Ordinal) ? "grouping and search" : "grouping"
            : query.Contains("&search=", StringComparison.Ordinal) ? "search"
            : query.Contains("&filter=", StringComparison.Ordinal) ? "filter" : "everything";

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
        if (at < 0)
        {
            return null;
        }
        var value = query[(at + "&groupBy=".Length)..];
        var end = value.IndexOf('&', StringComparison.Ordinal);
        return Uri.UnescapeDataString(end < 0 ? value : value[..end]);
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
        var aggregates = list.Columns.Where(c => c.Aggregate && c.Type != ListColumnType.Money).Select(c => c.Key).ToList();
        var money = list.Columns.Where(c => c.Aggregate && c.Type == ListColumnType.Money).ToList();
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
        // Money totals, per currency: each currency's sum equals that of the walked rows of the group
        // in that currency, and no currency is missing or added.
        foreach (var group in groups.EnumerateArray())
        {
            var key = group.TryGetProperty("key", out var k) ? k.GetRawText() : "null";
            var mine = expected.GetValueOrDefault(key) ?? [];
            foreach (var moneyColumn in money)
            {
                var stated = new Dictionary<string, decimal>(StringComparer.Ordinal);
                if (group.TryGetProperty("moneyTotals", out var moneyTotals) && moneyTotals.ValueKind == JsonValueKind.Object &&
                    moneyTotals.TryGetProperty(moneyColumn.Key, out var lines) && lines.ValueKind == JsonValueKind.Array)
                {
                    foreach (var line in lines.EnumerateArray())
                    {
                        stated[line.TryGetProperty("currency", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : ""] = Number(line, "amount");
                    }
                }
                var walkedSums = mine.GroupBy(r => r.TryGetProperty(moneyColumn.CurrencyField!, out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : "", StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Sum(r => Number(r, moneyColumn.Key)), StringComparer.Ordinal);
                foreach (var currency in stated.Keys.Union(walkedSums.Keys))
                {
                    if (stated.GetValueOrDefault(currency) != walkedSums.GetValueOrDefault(currency) || stated.ContainsKey(currency) != walkedSums.ContainsKey(currency))
                    {
                        yield return $"group {key} totals {moneyColumn.Key} in '{currency}' = {stated.GetValueOrDefault(currency).ToString(CultureInfo.InvariantCulture)}, " +
                                     $"but the walked rows in that currency add up to {walkedSums.GetValueOrDefault(currency).ToString(CultureInfo.InvariantCulture)}";
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

    /// <summary>One tenant's keyset walk: every row, and every page as answered.</summary>
    private sealed record Walk(List<JsonElement> Rows, List<JsonElement> Pages)
    {
        public string? Next { get; set; }

        public bool Done { get; set; }
    }

    /// <summary>Every row of the query for both tenants, page by page with the keyset cursor (or,
    /// with <paramref name="offset"/>, by skipping the rows already returned), in lock step: tenant
    /// <paramref name="a"/>'s page n, then tenant <paramref name="b"/>'s page n. Null for a walk
    /// that was refused or did not finish.</summary>
    private static async Task<(Walk? A, Walk? B)> WalkTogetherAsync(HttpClient a, HttpClient b, string endpoint, string query, int take, bool offset = false)
    {
        var walks = new[] { new Walk([], []), new Walk([], []) };
        var clients = new[] { a, b };
        var failed = new bool[2];
        for (var page = 0; page < MaxPages && walks.Any(w => !w.Done); page++)
        {
            for (var i = 0; i < 2; i++)
            {
                var walk = walks[i];
                if (walk.Done)
                {
                    continue;
                }
                var uri = $"{endpoint}?take={take}{query}" + (offset
                    ? walk.Rows.Count == 0 ? "" : "&skip=" + walk.Rows.Count.ToString(CultureInfo.InvariantCulture)
                    : walk.Next is null ? "" : "&after=" + Uri.EscapeDataString(walk.Next));
                if (await GetAsync(clients[i], uri) is not { } answer)
                {
                    failed[i] = true;
                    walk.Done = true;
                    continue;
                }
                walk.Pages.Add(answer);
                walk.Rows.AddRange(answer.GetProperty("items").EnumerateArray());
                walk.Next = answer.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                walk.Done = walk.Next is null;
            }
        }
        return (failed[0] || !walks[0].Done ? null : walks[0], failed[1] || !walks[1].Done ? null : walks[1]);
    }
}
