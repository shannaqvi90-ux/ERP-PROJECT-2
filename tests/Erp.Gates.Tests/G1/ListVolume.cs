using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Data;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1 for list answers at demo volume (critic p05 round 9, plant L12). <see cref="ListAnswers"/>
/// judges every registered list in the gate fixture, where each tenant holds tens of rows; code that
/// runs only for a big list never runs there. The most natural such code is an estimated total
/// (pg_class.reltuples, the statistics views, a planner estimate from EXPLAIN), which counts every
/// tenant's rows because row-level security does not narrow catalog figures; others are a total or
/// group count kept only for queries too big to count every time, a deep page served from a
/// remembered position, or a broad search answered differently once it is too big to rank.
/// <para>
/// So here both tenants hold a demo volume of rows (<see cref="Erp.Kernel.Seeding.SeedPlan.GateAtVolume"/>)
/// and every registered list is judged against each tenant's own rows: each tenant walks the whole
/// list by keyset (lock step with the other tenant), which gives every row with its values; the walk
/// is anchored in the database (the tenant's own row count of the list's table, and every walked id
/// is a row of that tenant). Then every query of a set built from the list's definition (everything,
/// every sort key both ways, every choice and flag value, text filters, "is null" on every filterable
/// column, every grouping alone, sorted and filtered, one-letter searches in Latin and Arabic letters,
/// searches sorted and grouped) is sent by tenant B, tenant A, B and A again, the same request each
/// time, as a first page and at offsets deep in the list (half way, the last rows, and past
/// <see cref="ListSearch.MaxRankedRows"/>), and deep keyset cursors of each walk are followed by
/// both tenants. Each answer's total must be the number of the tenant's own rows
/// the query matches (computed from the walked rows), its rows must be among them (in the walk's
/// order, where the list's default order applies), and its groups must count them.
/// </para>
/// </summary>
public static class ListVolume
{
    /// <summary>Rows asked for on a judged first page (the API's default page).</summary>
    public const int Take = ListRequest.DefaultTake;

    /// <summary>Rows asked for on a judged page at an offset: the client's chunk when a user jumps
    /// through a long list (web/src/kernel/lists/useListRows.ts).</summary>
    public const int JumpTake = 100;

    /// <param name="Wrong">Answers that are not the asking tenant's own.</param>
    /// <param name="Blind">Reasons the check may have been blind.</param>
    /// <param name="Answers">Answers judged (each request of each tenant).</param>
    /// <param name="Discriminating">Queries whose true answers differ between the two tenants.</param>
    /// <param name="ListsAtVolume">Lists that held at least the volume in both tenants.</param>
    /// <param name="DeepAnswers">Answers judged at an offset past <see cref="ListSearch.MaxRankedRows"/>.</param>
    /// <param name="UnrankedSearches">Searches judged whose true answer is too big to rank, in both tenants.</param>
    /// <param name="RankedSearches">Searches judged that are ranked (some rows, at most <see cref="ListSearch.MaxRankedRows"/>), in both tenants.</param>
    /// <param name="CursorsFollowed">Deep keyset cursors followed (by both tenants).</param>
    public sealed record Result(IReadOnlyList<string> Wrong, IReadOnlyList<string> Blind, int Answers, int Queries, int Discriminating, int RowsWalked,
        int ListsAtVolume, int DeepAnswers, int UnrankedSearches, int RankedSearches, int CursorsFollowed, IReadOnlyList<string> Summary);

    /// <summary>One-letter searches: Latin letters (some in every row of a tenant through its e-mail
    /// domain or canary, some in few rows) and Arabic letters (an alef with its spellings among them).</summary>
    private static readonly string[] Letters = ["a", "s", "i", "t", "u", "g", "k", "j", "w", "q", "z", "م", "ا", "ق"];

    /// <summary>Text filters: letters in many rows, in few rows, and an Arabic letter.</summary>
    private static readonly string[] FilterLetters = ["a", "q", "م"];

    /// <summary>Every letter the check searches or filters by: the volume fixture's canary holds none
    /// of them, so the true counts do not depend on it.</summary>
    public static IReadOnlyList<string> CheckedLetters { get; } = [.. Letters.Concat(FilterLetters).Distinct(StringComparer.Ordinal)];

    private sealed record Query(string Text, string Kind, Func<JsonElement, bool> Matches, bool DefaultOrder, string? Search, string? GroupBy);

    /// <summary>One tenant's whole list: every row in the list's default order, and the cursor after each walked page.</summary>
    private sealed record Truth(string Who, List<JsonElement> Rows, IReadOnlyList<string?> Cursors, int WalkTake)
    {
        public HashSet<Guid> Ids { get; } = Rows.Select(Id).ToHashSet();
    }

    private sealed class Tally
    {
        public ConcurrentQueue<string> Wrong { get; } = new();
        public ConcurrentQueue<string> Blind { get; } = new();
        public List<string> Summary { get; } = [];
        public int Answers;
        public int Queries;
        public int Discriminating;
        public int RowsWalked;
        public int ListsAtVolume;
        public int DeepAnswers;
        public int UnrankedSearches;
        public int RankedSearches;
        public int CursorsFollowed;
    }

    /// <param name="env">The large-list environment (or a planted host over its database).</param>
    /// <param name="a">Tenant A's administrator.</param>
    /// <param name="b">Tenant B's administrator.</param>
    /// <param name="volume">Rows each tenant's main lists hold at least.</param>
    /// <param name="only">The lists to judge (every registered list when null).</param>
    public static async Task<Result> RunAsync(ErpTestEnvironment env, HttpClient a, HttpClient b, int volume, Func<ListDefinition, bool>? only = null)
    {
        var catalog = env.Factory.Services.GetRequiredService<ModuleCatalog>();
        await using var scope = env.Factory.Services.CreateAsyncScope();
        var models = catalog.DbContexts.Select(t => ((DbContext)scope.ServiceProvider.GetRequiredService(t)).Model).ToList();
        await using var admin = await env.OpenAdminAsync();
        var tally = new Tally();
        foreach (var binding in catalog.ListBindings.Where(l => only?.Invoke(l.Definition) ?? true))
        {
            var list = binding.Definition;
            // Each tenant walks the whole list in its default order, page n of A just before page n
            // of B; every page's total must be the walk's row count.
            var walkTake = ListRequest.MaxTake;
            var (walkA, walkB) = await ListAnswers.WalkTogetherAsync(a, b, list.Endpoint, "", walkTake, maxPages: 2 * volume / walkTake + 1000);
            if (walkA is null || walkB is null)
            {
                tally.Wrong.Enqueue($"list '{list.Key}': the keyset walk of GET {list.Endpoint} did not finish (A {(walkA is null ? "failed" : "ok")}, B {(walkB is null ? "failed" : "ok")})");
                continue;
            }
            var truths = new Dictionary<string, Truth>(StringComparer.Ordinal)
            {
                ["A"] = new("A", walkA.Rows, walkA.Pages.Select(NextOf).ToList(), walkTake),
                ["B"] = new("B", walkB.Rows, walkB.Pages.Select(NextOf).ToList(), walkTake),
            };
            Interlocked.Add(ref tally.RowsWalked, walkA.Rows.Count + walkB.Rows.Count);
            foreach (var (who, walk) in new[] { ("A", walkA), ("B", walkB) })
            {
                var count = walk.Rows.Count;
                for (var n = 0; n < walk.Pages.Count; n++)
                {
                    var pageTotal = walk.Pages[n].GetProperty("total").GetInt32();
                    if (pageTotal != count)
                    {
                        tally.Wrong.Enqueue($"tenant {who}: page {n + 1} of {walk.Pages.Count} of the keyset walk of GET {list.Endpoint}?take={walkTake} answered total {pageTotal}, but the walk returns {count} rows");
                    }
                }
                if (truths[who].Ids.Count != count)
                {
                    tally.Wrong.Enqueue($"tenant {who}: the keyset walk of GET {list.Endpoint} returned {count - truths[who].Ids.Count} rows twice");
                }
            }
            foreach (var id in truths["A"].Ids.Intersect(truths["B"].Ids).Take(3))
            {
                tally.Wrong.Enqueue($"list '{list.Key}': row {id} was walked by both tenants");
            }
            await AnchorAsync(tally, admin, models, binding, env, truths);
            var atVolume = walkA.Rows.Count >= volume && walkB.Rows.Count >= volume;
            if (atVolume)
            {
                Interlocked.Increment(ref tally.ListsAtVolume);
            }

            var listTally = new ListTally();
            var queries = QueriesFor(list).ToList();
            await Parallel.ForEachAsync(queries, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (query, _) =>
                await JudgeQueryAsync(tally, listTally, list, a, b, truths, query));
            await FollowCursorsAsync(tally, list, a, b, truths);

            if (atVolume)
            {
                if (listTally.Discriminating == 0)
                {
                    tally.Blind.Enqueue($"list '{list.Key}': no query had different true answers in the two tenants");
                }
                if (listTally.Deep == 0)
                {
                    tally.Blind.Enqueue($"list '{list.Key}': no answer was judged at an offset past {ListSearch.MaxRankedRows} rows");
                }
                if (list.SearchFields.Count > 0 && listTally.Unranked == 0)
                {
                    tally.Blind.Enqueue($"list '{list.Key}': no search matched more than {ListSearch.MaxRankedRows} rows in both tenants with different counts, so a broad search's total went unjudged");
                }
                if (list.SearchFields.Count > 0 && listTally.Ranked == 0)
                {
                    tally.Blind.Enqueue($"list '{list.Key}': no search matched between 1 and {ListSearch.MaxRankedRows} rows in both tenants, so a ranked search over a big list went unjudged");
                }
            }
            tally.Summary.Add($"list '{list.Key}': walked A {walkA.Rows.Count} and B {walkB.Rows.Count} rows{(atVolume ? " (at volume)" : "")}; {queries.Count} queries, " +
                              $"{listTally.Discriminating} with different true answers, {listTally.Deep} deep answers, {listTally.Unranked} unranked and {listTally.Ranked} ranked searches" +
                              (listTally.SearchCounts.IsEmpty ? "" : "; searches (A/B rows): " + string.Join(", ", listTally.SearchCounts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Value))));
        }
        return new Result(tally.Wrong.ToList(), tally.Blind.ToList(), tally.Answers, tally.Queries, tally.Discriminating, tally.RowsWalked, tally.ListsAtVolume,
            tally.DeepAnswers, tally.UnrankedSearches, tally.RankedSearches, tally.CursorsFollowed, tally.Summary);
    }

    private sealed class ListTally
    {
        public int Discriminating;
        public int Deep;
        public int Unranked;
        public int Ranked;

        /// <summary>Each one-letter search with its true counts in A and B, for the summary.</summary>
        public ConcurrentDictionary<string, string> SearchCounts { get; } = new(StringComparer.Ordinal);
    }

    private static string? NextOf(JsonElement page) =>
        page.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;

    private static Guid Id(JsonElement row) => row.GetProperty("id").GetGuid();

    /// <summary>
    /// The walks are each tenant's whole list: for a list over a table, the tenant's own row count
    /// of the table (read as the superuser, tenant by tenant) equals the walk, and every walked id is
    /// one of that tenant's rows. A list whose rows are not a table's is read in memory (the index
    /// gate reviews why) and its walk stands alone.
    /// </summary>
    private static async Task AnchorAsync(Tally tally, Npgsql.NpgsqlConnection admin, IReadOnlyList<Microsoft.EntityFrameworkCore.Metadata.IModel> models,
        IListBinding binding, ErpTestEnvironment env, IReadOnlyDictionary<string, Truth> truths)
    {
        var list = binding.Definition;
        var entity = models.Select(m => m.FindEntityType(binding.RowType)).FirstOrDefault(e => e is not null);
        if (entity is null)
        {
            if (binding.InMemoryReason is null)
            {
                tally.Blind.Enqueue($"list '{list.Key}': its rows ({binding.RowType.Name}) are no table's, so the walk could not be anchored in the database");
            }
            return;
        }
        var table = $"{TenantSql.Identifier(entity.GetSchema() ?? entity.Model.GetDefaultSchema() ?? "public")}.{TenantSql.Identifier(entity.GetTableName()!)}";
        foreach (var (who, tenant) in new[] { ("A", env.TenantA), ("B", env.TenantB) })
        {
            var truth = truths[who];
            var rows = await DbCatalog.ScalarAsync<long>(admin, $"SELECT count(*) FROM {table} WHERE tenant_id = @t", ("t", tenant.Id));
            if (rows != truth.Rows.Count)
            {
                tally.Wrong.Enqueue($"tenant {who}: the keyset walk of GET {list.Endpoint} returned {truth.Rows.Count} rows, but the tenant holds {rows} rows in {table}");
            }
            var own = await DbCatalog.ScalarAsync<long>(admin, $"SELECT count(*) FROM {table} WHERE tenant_id = @t AND id = ANY(@ids)",
                ("t", tenant.Id), ("ids", truth.Ids.ToArray()));
            if (own != truth.Ids.Count)
            {
                tally.Wrong.Enqueue($"tenant {who}: {truth.Ids.Count - own} rows of the keyset walk of GET {list.Endpoint} are not the tenant's rows of {table}");
            }
        }
    }

    /// <summary>The queries judged for a list, each with what makes a row match it.</summary>
    private static IEnumerable<Query> QueriesFor(ListDefinition list)
    {
        static bool All(JsonElement _) => true;
        var sortKeys = ListAnswers.SortKeys(list);
        yield return new Query("", "everything", All, true, null, null);
        foreach (var key in sortKeys)
        {
            yield return new Query("&sort=" + Uri.EscapeDataString(key), "sorted", All, false, null, null);
        }
        var filters = new List<(string Filter, Func<JsonElement, bool> Matches)>();
        foreach (var column in list.Columns.Where(c => c.Filterable))
        {
            var key = column.Key;
            switch (column.Type)
            {
                case ListColumnType.Choice:
                    foreach (var choice in column.Choices ?? [])
                    {
                        filters.Add((ListFilterText.Eq(key, choice.Value), r => Text(r, key) == choice.Value));
                    }
                    break;
                case ListColumnType.Boolean:
                    filters.Add((ListFilterText.Eq(key, true), r => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True));
                    filters.Add((ListFilterText.Eq(key, false), r => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.False));
                    break;
                case ListColumnType.Text:
                    foreach (var letter in FilterLetters)
                    {
                        filters.Add(($"{key} contains {ListFilterText.Quote(letter)}", r => Text(r, key) is { } text && Lower(text).Contains(Lower(letter), StringComparison.Ordinal)));
                    }
                    break;
            }
            filters.Add(($"{key} is null", r => !r.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null));
        }
        foreach (var (filter, matches) in filters)
        {
            yield return new Query("&filter=" + Uri.EscapeDataString(filter), "filter", matches, true, null, null);
        }
        foreach (var column in list.Columns.Where(c => c.Groupable))
        {
            yield return new Query("&groupBy=" + column.Key, "grouping", All, false, null, column.Key);
            if (sortKeys.Count > 0)
            {
                yield return new Query($"&groupBy={column.Key}&sort={Uri.EscapeDataString(sortKeys[^1])}", "sorted grouping", All, false, null, column.Key);
            }
            if (filters.Count > 0)
            {
                var (filter, matches) = filters[0];
                yield return new Query($"&groupBy={column.Key}&filter={Uri.EscapeDataString(filter)}", "filtered grouping", matches, false, null, column.Key);
            }
        }
        if (list.SearchFields.Count == 0)
        {
            yield break;
        }
        foreach (var letter in Letters)
        {
            yield return new Query("&search=" + Uri.EscapeDataString(letter), "search", SearchMatch(list, letter), true, letter, null);
        }
        foreach (var letter in new[] { "a", "k" })
        {
            foreach (var key in sortKeys.Take(2))
            {
                yield return new Query($"&search={letter}&sort={Uri.EscapeDataString(key)}", "sorted search", SearchMatch(list, letter), false, letter, null);
            }
            foreach (var column in list.Columns.Where(c => c.Groupable))
            {
                yield return new Query($"&search={letter}&groupBy={column.Key}", "grouped search", SearchMatch(list, letter), false, letter, column.Key);
            }
        }
    }

    /// <summary>A one-word search: the word, in any of its spellings (<see cref="ListSearch.Spellings"/>),
    /// in any of the fields a word like it searches, in any case.</summary>
    private static Func<JsonElement, bool> SearchMatch(ListDefinition list, string word)
    {
        var fields = list.SearchFieldsFor(word);
        var spellings = ListSearch.Spellings(word).Select(Lower).ToList();
        return row => fields.Any(f => Text(row, f) is { } text && spellings.Any(s => Lower(text).Contains(s, StringComparison.Ordinal)));
    }

    private static string Lower(string text) => text.ToLowerInvariant();

    private static string? Text(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// One query: its first page and its pages at deep offsets, each sent by tenant B, tenant A, B
    /// and A again (so every answer but the first comes right after the other tenant's identical
    /// request, both ways round); every answer judged against the asking tenant's own rows.
    /// </summary>
    private static async Task JudgeQueryAsync(Tally tally, ListTally listTally, ListDefinition list, HttpClient a, HttpClient b,
        IReadOnlyDictionary<string, Truth> truths, Query query)
    {
        var expected = truths.ToDictionary(t => t.Key, t => t.Value.Rows.Where(query.Matches).ToList(), StringComparer.Ordinal);
        var matchingIds = expected.ToDictionary(e => e.Key, e => e.Value.Select(Id).ToHashSet(), StringComparer.Ordinal);
        var countA = expected["A"].Count;
        var countB = expected["B"].Count;
        Interlocked.Increment(ref tally.Queries);
        if (countA != countB)
        {
            Interlocked.Increment(ref tally.Discriminating);
            Interlocked.Increment(ref listTally.Discriminating);
        }
        if (query.Kind == "search" && query.Search is not null)
        {
            listTally.SearchCounts[query.Search] = $"{query.Search} {countA}/{countB}";
        }
        if (query.Search is not null && query.GroupBy is null && query.Kind == "search" && countA != countB)
        {
            if (countA > ListSearch.MaxRankedRows && countB > ListSearch.MaxRankedRows)
            {
                Interlocked.Increment(ref tally.UnrankedSearches);
                Interlocked.Increment(ref listTally.Unranked);
            }
            else if (countA is > 0 and <= ListSearch.MaxRankedRows && countB is > 0 and <= ListSearch.MaxRankedRows)
            {
                Interlocked.Increment(ref tally.RankedSearches);
                Interlocked.Increment(ref listTally.Ranked);
            }
        }
        // The offsets: the first page, half way, the last rows, and past the ranking limit; the
        // larger tenant's offsets are sent by both (the smaller one's answer is then past its end).
        var largest = Math.Max(countA, countB);
        var skips = new SortedSet<int> { 0, largest / 2, Math.Max(0, largest - 3), Math.Min(countA, countB) / 2 };
        if (ListSearch.MaxRankedRows + 1 < largest)
        {
            skips.Add(ListSearch.MaxRankedRows + 1);
        }
        if (query.Kind == "everything")
        {
            skips.Add(largest);
        }
        foreach (var skip in skips)
        {
            var take = skip > 0 ? JumpTake : Take;
            var uri = $"{list.Endpoint}?take={take}{query.Text}" + (skip > 0 ? "&skip=" + skip.ToString(CultureInfo.InvariantCulture) : "");
            // B, A, B, A: every answer but the first comes right after the other tenant's identical request.
            foreach (var (who, client) in new[] { ("B", b), ("A", a), ("B", b), ("A", a) })
            {
                var answer = await ListAnswers.GetAsync(client, uri);
                Interlocked.Increment(ref tally.Answers);
                if (answer is null)
                {
                    tally.Wrong.Enqueue($"tenant {who}: GET {uri} was not answered with 200");
                    continue;
                }
                if (skip > ListSearch.MaxRankedRows)
                {
                    Interlocked.Increment(ref tally.DeepAnswers);
                    Interlocked.Increment(ref listTally.Deep);
                }
                foreach (var problem in Judge(list, query, answer.Value, expected[who], matchingIds[who], truths[who], truths[who == "A" ? "B" : "A"], skip, take))
                {
                    tally.Wrong.Enqueue($"tenant {who}: GET {uri} {problem} (the other tenant's own rows: {expected[who == "A" ? "B" : "A"].Count})");
                }
            }
        }
    }

    /// <summary>What is wrong with one answer, judged against the asking tenant's rows the query matches.</summary>
    private static IEnumerable<string> Judge(ListDefinition list, Query query, JsonElement answer, List<JsonElement> mine, HashSet<Guid> matching, Truth own, Truth other, int skip, int take)
    {
        var total = answer.GetProperty("total").GetInt32();
        if (total != mine.Count)
        {
            yield return $"answered total {total}, but the tenant's own rows matching it are {mine.Count}";
        }
        var items = answer.GetProperty("items").EnumerateArray().Select(Id).ToList();
        var expectedCount = Math.Clamp(mine.Count - skip, 0, take);
        if (items.Count != expectedCount)
        {
            yield return $"returned {items.Count} rows, but {expectedCount} of the tenant's own matching rows lie at that offset";
        }
        foreach (var id in items.Where(i => !matching.Contains(i)).Take(3))
        {
            yield return $"returned row {id}, which is not one of the tenant's rows matching the query" + (other.Ids.Contains(id) ? " (it is the other tenant's)" : own.Ids.Contains(id) ? " (it is the tenant's own, but does not match)" : "");
        }
        // The list's default order is the walk's: a query in that order (no sort, no grouping, and a
        // search too broad to rank) answers exactly the walk's matching rows at the offset.
        var defaultOrder = query.DefaultOrder && (query.Search is null || mine.Count > ListSearch.MaxRankedRows);
        if (defaultOrder && items.Count == expectedCount)
        {
            var slice = mine.Skip(skip).Take(take).Select(Id).ToList();
            if (!slice.SequenceEqual(items))
            {
                yield return $"returned rows other than the tenant's matching rows {skip + 1} to {skip + slice.Count} in the list's default order";
            }
        }
        if (query.GroupBy is { } column)
        {
            if (answer.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
            {
                foreach (var problem in ListAnswers.JudgeGroups(list, column, groups, mine, mine.Count))
                {
                    yield return problem;
                }
            }
            else
            {
                yield return $"grouped by {column} but carries no groups";
            }
        }
    }

    /// <summary>Deep keyset cursors of each tenant's walk (a quarter, half and three quarters of the
    /// way), each followed by the other tenant, the tenant and the other tenant again: the tenant's
    /// answer is exactly the walk's next rows with the whole list's total; the other tenant's answer
    /// (a position in its own rows) is its own rows with its own total.</summary>
    private static async Task FollowCursorsAsync(Tally tally, ListDefinition list, HttpClient a, HttpClient b, IReadOnlyDictionary<string, Truth> truths)
    {
        foreach (var (owner, other) in new[] { ("A", "B"), ("B", "A") })
        {
            var truth = truths[owner];
            var pages = truth.Cursors.Count;
            foreach (var at in new[] { pages / 4, pages / 2, 3 * pages / 4 }.Distinct().Where(p => p < pages - 1 && truth.Cursors[p] is not null))
            {
                var uri = $"{list.Endpoint}?take={Take}&after={Uri.EscapeDataString(truth.Cursors[at]!)}";
                foreach (var who in new[] { other, owner, other })
                {
                    var answer = await ListAnswers.GetAsync(who == "A" ? a : b, uri);
                    Interlocked.Increment(ref tally.Answers);
                    if (answer is null)
                    {
                        tally.Wrong.Enqueue($"tenant {who}: GET {uri} (tenant {owner}'s cursor after row {(at + 1) * truth.WalkTake}) was not answered with 200");
                        continue;
                    }
                    var mine = truths[who];
                    var total = answer.Value.GetProperty("total").GetInt32();
                    if (total != mine.Rows.Count)
                    {
                        tally.Wrong.Enqueue($"tenant {who}: GET {uri} (tenant {owner}'s cursor) answered total {total}, but the tenant's own rows are {mine.Rows.Count}");
                    }
                    var items = answer.Value.GetProperty("items").EnumerateArray().Select(Id).ToList();
                    foreach (var id in items.Where(i => !mine.Ids.Contains(i)).Take(3))
                    {
                        tally.Wrong.Enqueue($"tenant {who}: GET {uri} (tenant {owner}'s cursor) returned row {id}, which is not the tenant's own");
                    }
                    if (who == owner)
                    {
                        var start = (at + 1) * truth.WalkTake;
                        var slice = truth.Rows.Skip(start).Take(Take).Select(Id).ToList();
                        if (!slice.SequenceEqual(items))
                        {
                            tally.Wrong.Enqueue($"tenant {who}: GET {uri} (its own cursor after row {start}) returned rows other than its rows {start + 1} to {start + slice.Count}");
                        }
                    }
                }
                Interlocked.Increment(ref tally.CursorsFollowed);
            }
        }
    }
}
