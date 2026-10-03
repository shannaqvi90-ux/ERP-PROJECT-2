using System.Globalization;
using System.Net;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.Rules;

/// <summary>
/// Every registered list keeps the promises its registration makes, through its real endpoint:
/// the list query contract is documented (search, filter, sort, after, skip, take, groupBy; pages
/// with items, total and next), every sortable column orders the list both ways and pages by
/// keyset exactly as one page does, every filterable column filters, every search field is
/// searched, grouping counts every row once, and what the registration does not allow (unknown,
/// unsortable, unfilterable, ungroupable columns) is refused with a 400 naming the parameter
/// rather than silently ignored. The screens read each list's definition from
/// <c>/api/lists/{key}/definition</c>, which must match the registration.
/// </summary>
public sealed class ListContractGateTests(GateFixture fixture)
{
    private static readonly string[] ContractParameters = ["filter", "sort", "after", "skip", "take"];

    private ModuleCatalog Catalog => fixture.Env.Factory.Services.GetRequiredService<ModuleCatalog>();

    [Fact]
    public async Task Every_registered_list_documents_the_list_query_contract_and_its_definition()
    {
        using var client = fixture.Env.CreateClient();
        var document = await OpenApiDocument.LoadAsync(client);
        using var admin = await fixture.Env.SignInAsync(fixture.Env.Email(fixture.Env.TenantA, "admin"));
        var problems = new List<string>();
        foreach (var list in Catalog.Lists)
        {
            var parameters = document.Parameters("GET", list.Endpoint).Where(p => p.In == "query").Select(p => p.Name).ToHashSet();
            var expected = ContractParameters.AsEnumerable();
            if (list.SearchFields.Count > 0) expected = expected.Append("search");
            if (list.Columns.Any(c => c.Groupable)) expected = expected.Append("groupBy");
            problems.AddRange(expected.Where(p => !parameters.Contains(p)).Select(p => $"list '{list.Key}': GET {list.Endpoint} does not document the '{p}' parameter"));
            if (document.TryGetOperation("GET", list.Endpoint, out var operation))
            {
                var page = document.Resolve(operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
                var properties = page.GetProperty("properties");
                foreach (var name in new[] { "items", "total", "next" }.Concat(list.Columns.Any(c => c.Groupable) ? ["groups"] : []))
                {
                    if (!properties.TryGetProperty(name, out _)) problems.Add($"list '{list.Key}': its page has no '{name}'");
                }
            }
            if (Catalog.ListBindings.All(b => b.Definition.Key != list.Key))
            {
                problems.Add($"list '{list.Key}': no query binding");
            }
            var definitionPath = $"/api/lists/{list.Key}/definition";
            if (!document.TryGetOperation("GET", definitionPath, out _))
            {
                problems.Add($"list '{list.Key}': GET {definitionPath} is not documented");
                continue;
            }
            var definition = await GetAsync(admin, definitionPath);
            var columns = definition.GetProperty("columns").EnumerateArray().ToList();
            var described = columns.Select(c => $"{c.GetProperty("key").GetString()}:{c.GetProperty("type").GetString()}:{c.GetProperty("sortable").GetBoolean()}:{c.GetProperty("filterable").GetBoolean()}:{c.GetProperty("groupable").GetBoolean()}");
            var registered = list.Columns.Select(c => $"{c.Key}:{JsonNamingPolicy.CamelCase.ConvertName(c.Type.ToString())}:{c.Sortable}:{c.Filterable}:{c.Groupable}");
            if (!described.SequenceEqual(registered))
            {
                problems.Add($"list '{list.Key}': definition columns [{string.Join(", ", described)}] differ from the registration [{string.Join(", ", registered)}]");
            }
            if (definition.GetProperty("endpoint").GetString() != list.Endpoint)
            {
                problems.Add($"list '{list.Key}': definition names endpoint {definition.GetProperty("endpoint").GetString()}");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task Every_sortable_column_orders_its_list_both_ways_and_keyset_paging_matches_one_page()
    {
        using var admin = await fixture.Env.SignInAsync(fixture.Env.Email(fixture.Env.TenantA, "admin"));
        var problems = new List<string>();
        var checks = 0;
        foreach (var list in Catalog.Lists)
        {
            foreach (var column in list.Columns.Where(c => c.Sortable))
            {
                foreach (var descending in new[] { false, true })
                {
                    var sort = (descending ? "-" : "") + column.Key;
                    var page = await GetAsync(admin, $"{list.Endpoint}?take=200&sort={Uri.EscapeDataString(sort)}");
                    var rows = Items(page);
                    if (page.GetProperty("total").GetInt32() > rows.Count)
                    {
                        problems.Add($"list '{list.Key}': the gate fixture holds more rows than one page; widen the check");
                        continue;
                    }
                    var values = rows.Select(r => r.GetProperty(column.Key)).Where(v => v.ValueKind != JsonValueKind.Null).ToList();
                    if (column.Type != ListColumnType.Reference && !Ordered(values, column.Type, descending))
                    {
                        problems.Add($"list '{list.Key}': sort={sort} does not order the rows by {column.Key}: {string.Join(" | ", values.Take(12).Select(v => v.ToString()))}");
                    }
                    var walked = new List<string>();
                    string? next = null;
                    var pages = 0;
                    do
                    {
                        var slice = await GetAsync(admin, $"{list.Endpoint}?take=5&sort={Uri.EscapeDataString(sort)}" + (next is null ? "" : $"&after={Uri.EscapeDataString(next)}"));
                        walked.AddRange(Items(slice).Select(r => r.GetProperty("id").GetString()!));
                        next = slice.GetProperty("next").ValueKind == JsonValueKind.String ? slice.GetProperty("next").GetString() : null;
                    }
                    while (next is not null && ++pages < 100);
                    if (!walked.SequenceEqual(rows.Select(r => r.GetProperty("id").GetString()!)))
                    {
                        problems.Add($"list '{list.Key}': sort={sort}: keyset paging (after) returned {walked.Count} rows that differ from one page of {rows.Count}");
                    }
                    checks++;
                }
                var up = Items(await GetAsync(admin, $"{list.Endpoint}?take=200&sort={column.Key}")).Select(r => r.GetProperty(column.Key).ToString()).ToList();
                var down = Items(await GetAsync(admin, $"{list.Endpoint}?take=200&sort=-{column.Key}")).Select(r => r.GetProperty(column.Key).ToString()).ToList();
                if (up.Distinct().Count() > 1 && up.SequenceEqual(down))
                {
                    problems.Add($"list '{list.Key}': sort={column.Key} and sort=-{column.Key} return the same order; the sort is ignored");
                }
            }
            foreach (var column in list.Columns.Where(c => !c.Sortable))
            {
                await ExpectRefusedAsync(admin, $"{list.Endpoint}?sort={column.Key}", "sort", list, problems);
                checks++;
            }
            await ExpectRefusedAsync(admin, $"{list.Endpoint}?sort=zzzNoSuchColumn", "sort", list, problems);
            await ExpectRefusedAsync(admin, $"{list.Endpoint}?after=zzz-not-a-cursor", "after", list, problems);
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checks >= Ratchet.Min("rules.listSortChecks"), $"{checks} sort checks; ratchet minimum {Ratchet.Min("rules.listSortChecks")}");
    }

    [Fact]
    public async Task Every_filterable_column_filters_its_list_and_other_columns_are_refused()
    {
        using var admin = await fixture.Env.SignInAsync(fixture.Env.Email(fixture.Env.TenantA, "admin"));
        var problems = new List<string>();
        var checks = 0;
        foreach (var list in Catalog.Lists)
        {
            var all = Items(await GetAsync(admin, $"{list.Endpoint}?take=200"));
            var total = all.Count;
            foreach (var column in list.Columns.Where(c => c.Filterable))
            {
                var present = all.Select(r => r.GetProperty(column.Key)).Where(v => v.ValueKind != JsonValueKind.Null).ToList();
                var nulls = all.Count(r => r.GetProperty(column.Key).ValueKind == JsonValueKind.Null);
                async Task<List<JsonElement>> Filter(string filter)
                {
                    checks++;
                    return Items(await GetAsync(admin, $"{list.Endpoint}?take=200&filter={Uri.EscapeDataString(filter)}"));
                }
                void Expect(string filter, List<JsonElement> rows, Func<JsonElement, bool> holds, int? count = null)
                {
                    var wrong = rows.Where(r => !holds(r.GetProperty(column.Key))).ToList();
                    if (wrong.Count > 0)
                    {
                        problems.Add($"list '{list.Key}': filter {filter} returned {wrong.Count} rows that do not match: {wrong[0].GetProperty(column.Key)}");
                    }
                    if (count is { } expected && rows.Count != expected)
                    {
                        problems.Add($"list '{list.Key}': filter {filter} returned {rows.Count} rows, the list holds {expected} matching ones");
                    }
                }

                Expect($"{column.Key} is null", await Filter($"{column.Key} is null"), v => v.ValueKind == JsonValueKind.Null, nulls);
                Expect($"{column.Key} is not null", await Filter($"{column.Key} is not null"), v => v.ValueKind != JsonValueKind.Null, total - nulls);
                if (present.Count == 0)
                {
                    problems.Add($"list '{list.Key}': no row of the gate fixture has a {column.Key}; seed one so the filter can be checked");
                    continue;
                }
                switch (column.Type)
                {
                    case ListColumnType.Text:
                    {
                        var value = present[present.Count / 2].GetString()!;
                        var expected = present.Count(v => string.Equals(v.GetString(), value, StringComparison.OrdinalIgnoreCase));
                        Expect($"{column.Key} eq …", await Filter($"{column.Key} eq {ListFilterText.Quote(value.ToUpperInvariant())}"),
                            v => string.Equals(v.GetString(), value, StringComparison.OrdinalIgnoreCase), expected);
                        var part = value.Length > 4 ? value.Substring(1, Math.Min(5, value.Length - 2)) : value;
                        Expect($"{column.Key} contains …", await Filter($"{column.Key} contains {ListFilterText.Quote(part)}"),
                            v => v.GetString()!.Contains(part, StringComparison.OrdinalIgnoreCase), present.Count(v => v.GetString()!.Contains(part, StringComparison.OrdinalIgnoreCase)));
                        break;
                    }
                    case ListColumnType.Choice:
                        foreach (var choice in column.Choices ?? [])
                        {
                            Expect($"{column.Key} eq '{choice.Value}'", await Filter($"{column.Key} eq {ListFilterText.Quote(choice.Value)}"),
                                v => v.GetString() == choice.Value, present.Count(v => v.GetString() == choice.Value));
                        }
                        break;
                    case ListColumnType.Boolean:
                        foreach (var flag in new[] { true, false })
                        {
                            Expect($"{column.Key} eq {flag}", await Filter($"{column.Key} eq {(flag ? "true" : "false")}"),
                                v => v.GetBoolean() == flag, present.Count(v => v.GetBoolean() == flag));
                        }
                        break;
                    case ListColumnType.Number or ListColumnType.Money:
                    {
                        var numbers = present.Select(Number).Order().ToList();
                        var pivot = numbers[numbers.Count / 2];
                        var literal = pivot.ToString(CultureInfo.InvariantCulture);
                        Expect($"{column.Key} ge {literal}", await Filter($"{column.Key} ge {literal}"), v => Number(v) >= pivot, numbers.Count(n => n >= pivot));
                        Expect($"{column.Key} lt {literal}", await Filter($"{column.Key} lt {literal}"), v => Number(v) < pivot, numbers.Count(n => n < pivot));
                        break;
                    }
                    case ListColumnType.Date or ListColumnType.DateTime:
                    {
                        var instants = present.Select(Instant).Order().ToList();
                        var pivot = instants[instants.Count / 2];
                        var literal = column.Type == ListColumnType.Date ? pivot.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : pivot.ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);
                        Expect($"{column.Key} ge {literal}", await Filter($"{column.Key} ge {ListFilterText.Quote(literal)}"), v => Instant(v) >= pivot, instants.Count(n => n >= pivot));
                        Expect($"{column.Key} lt {literal}", await Filter($"{column.Key} lt {ListFilterText.Quote(literal)}"), v => Instant(v) < pivot, instants.Count(n => n < pivot));
                        break;
                    }
                    case ListColumnType.Reference:
                    {
                        var value = present[0].GetString()!;
                        Expect($"{column.Key} eq …", await Filter($"{column.Key} eq {ListFilterText.Quote(value)}"), v => v.GetString() == value, present.Count(v => v.GetString() == value));
                        break;
                    }
                }
            }
            foreach (var column in list.Columns.Where(c => !c.Filterable))
            {
                await ExpectRefusedAsync(admin, $"{list.Endpoint}?filter={Uri.EscapeDataString($"{column.Key} is null")}", "filter", list, problems);
                checks++;
            }
            await ExpectRefusedAsync(admin, $"{list.Endpoint}?filter={Uri.EscapeDataString("zzzNoSuchColumn is null")}", "filter", list, problems);
            await ExpectRefusedAsync(admin, $"{list.Endpoint}?filter={Uri.EscapeDataString("(((")}", "filter", list, problems);
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checks >= Ratchet.Min("rules.listFilterChecks"), $"{checks} filter checks; ratchet minimum {Ratchet.Min("rules.listFilterChecks")}");
    }

    [Fact]
    public async Task Every_search_field_is_searched_word_by_word_and_grouping_counts_every_row_once()
    {
        using var admin = await fixture.Env.SignInAsync(fixture.Env.Email(fixture.Env.TenantA, "admin"));
        var problems = new List<string>();
        var checks = 0;
        foreach (var list in Catalog.Lists)
        {
            var all = Items(await GetAsync(admin, $"{list.Endpoint}?take=200"));
            foreach (var field in list.SearchFields)
            {
                var row = all.FirstOrDefault(r => r.GetProperty(field).ValueKind == JsonValueKind.String &&
                                                  r.GetProperty(field).GetString()!.Split(' ').Any(w => w.Length >= 3));
                if (row.ValueKind == JsonValueKind.Undefined)
                {
                    problems.Add($"list '{list.Key}': no row has a word in search field {field}");
                    continue;
                }
                var words = row.GetProperty(field).GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).Take(2).ToList();
                var search = string.Join(' ', words.Select(w => w.ToUpperInvariant()).Reverse());
                var found = Items(await GetAsync(admin, $"{list.Endpoint}?take=200&search={Uri.EscapeDataString(search)}"));
                if (found.All(r => r.GetProperty("id").GetString() != row.GetProperty("id").GetString()))
                {
                    problems.Add($"list '{list.Key}': searching '{search}' does not find the row whose {field} holds those words");
                }
                foreach (var hit in found)
                {
                    var text = string.Join(" ", list.SearchFields.Select(f => hit.GetProperty(f).ToString())).ToLowerInvariant();
                    if (words.Any(w => !text.Contains(w.ToLowerInvariant(), StringComparison.Ordinal)))
                    {
                        problems.Add($"list '{list.Key}': searching '{search}' returned a row without every word in its search fields");
                        break;
                    }
                }
                checks++;
            }
            if (list.SearchFields.Count == 0)
            {
                await ExpectRefusedAsync(admin, $"{list.Endpoint}?search=anything", "search", list, problems);
            }
            foreach (var column in list.Columns.Where(c => c.Groupable))
            {
                var page = await GetAsync(admin, $"{list.Endpoint}?take=1&groupBy={column.Key}");
                var groups = page.GetProperty("groups").EnumerateArray().ToList();
                var counted = groups.Sum(g => g.GetProperty("count").GetInt32());
                if (counted != page.GetProperty("total").GetInt32())
                {
                    problems.Add($"list '{list.Key}': groupBy={column.Key} counts {counted} rows in its groups, the list holds {page.GetProperty("total").GetInt32()}");
                }
                if (groups.Select(g => g.GetProperty("key").ToString()).Distinct().Count() != groups.Count)
                {
                    problems.Add($"list '{list.Key}': groupBy={column.Key} returns a key twice");
                }
                foreach (var total in list.Columns.Where(c => c.Aggregate))
                {
                    var sum = all.Sum(r => r.GetProperty(total.Key).ValueKind == JsonValueKind.Null ? 0 : Number(r.GetProperty(total.Key)));
                    var grouped = groups.Sum(g => decimal.Parse(g.GetProperty("totals").GetProperty(total.Key).GetString()!, CultureInfo.InvariantCulture));
                    if (sum != grouped)
                    {
                        problems.Add($"list '{list.Key}': groupBy={column.Key} totals {total.Key} to {grouped}, the rows add up to {sum}");
                    }
                }
                checks++;
            }
            foreach (var column in list.Columns.Where(c => !c.Groupable))
            {
                await ExpectRefusedAsync(admin, $"{list.Endpoint}?groupBy={column.Key}", "groupBy", list, problems);
                checks++;
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checks >= Ratchet.Min("rules.listSearchAndGroupChecks"), $"{checks} search and grouping checks; ratchet minimum {Ratchet.Min("rules.listSearchAndGroupChecks")}");
    }

    private static async Task ExpectRefusedAsync(HttpClient client, string uri, string parameter, ListDefinition list, List<string> problems)
    {
        using var response = await client.GetAsync(uri);
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            problems.Add($"list '{list.Key}': GET {uri} answered {(int)response.StatusCode}; it must be refused (400), not ignored");
            return;
        }
        using var problem = JsonDocument.Parse(text);
        if (!problem.RootElement.TryGetProperty("errors", out var errors) || !errors.TryGetProperty(parameter, out _))
        {
            problems.Add($"list '{list.Key}': GET {uri} was refused without naming the '{parameter}' parameter: {text}");
        }
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string uri)
    {
        using var response = await client.GetAsync(uri);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {uri}: {(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static List<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray().ToList();

    private static decimal Number(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture)
        : value.GetDecimal();

    private static DateTimeOffset Instant(JsonElement value) =>
        DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    /// <summary>Non-null values in order. Text may follow the database's collation: ordinal (C)
    /// or culture-aware ignoring case.</summary>
    private static bool Ordered(List<JsonElement> values, ListColumnType type, bool descending)
    {
        bool Check<T>(List<T> items, IComparer<T> comparer)
        {
            for (var i = 1; i < items.Count; i++)
            {
                var c = comparer.Compare(items[i - 1], items[i]);
                if (descending ? c < 0 : c > 0) return false;
            }
            return true;
        }
        return type switch
        {
            ListColumnType.Text or ListColumnType.Choice => Check(values.Select(v => v.GetString()!).ToList(), StringComparer.Ordinal) ||
                                                           Check(values.Select(v => v.GetString()!).ToList(), StringComparer.InvariantCultureIgnoreCase),
            ListColumnType.Number or ListColumnType.Money => Check(values.Select(Number).ToList(), Comparer<decimal>.Default),
            ListColumnType.Date or ListColumnType.DateTime => Check(values.Select(Instant).ToList(), Comparer<DateTimeOffset>.Default),
            ListColumnType.Boolean => Check(values.Select(v => v.GetBoolean()).ToList(), Comparer<bool>.Default),
            _ => true,
        };
    }
}
