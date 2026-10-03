using System.Text.Json;
using Erp.Kernel.Hosting;
using Erp.Kernel.Lists;
using Erp.Kernel.Localization;
using Erp.Kernel.Modules;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Kernel.Tests;

/// <summary>The list engine on its own: the filter language, registration checks, and the query
/// contract over rows in memory (sort with nulls and ties, keyset paging, grouping, errors).</summary>
public sealed class ListEngineTests
{
    public sealed record Item(Guid Id, string Name, string? Code, int Quantity, decimal Amount, bool Active, DateTimeOffset? Seen, DateOnly Day, string Kind);

    private static readonly ListDefinition Definition = new(
        "stock.items", "stock.items.title", "stock.items.read", "/api/stock/items",
        [
            new ListColumn("name", "stock.items.name", ListColumnType.Text, Sortable: true, Filterable: true),
            new ListColumn("code", "stock.items.code", ListColumnType.Text, Sortable: true, Filterable: true),
            new ListColumn("quantity", "stock.items.quantity", ListColumnType.Number, Sortable: true, Filterable: true, Aggregate: true),
            new ListColumn("amount", "stock.items.amount", ListColumnType.Money, Filterable: true, Aggregate: true),
            new ListColumn("active", "stock.items.active", ListColumnType.Boolean, Sortable: true, Filterable: true, Groupable: true),
            new ListColumn("seen", "stock.items.seen", ListColumnType.DateTime, Sortable: true, Filterable: true),
            new ListColumn("day", "stock.items.day", ListColumnType.Date, Filterable: true, Groupable: true),
            new ListColumn("kind", "stock.items.kind", ListColumnType.Choice, Filterable: true, Groupable: true,
                Choices: [new ListChoice("raw", "stock.kind.raw"), new ListChoice("finished", "stock.kind.finished")]),
        ],
        SearchFields: ["name", "code"],
        DefaultSort: "name",
        Presets: [new ListPreset("active", "stock.items.view.active", Filter: "active eq true")]);

    private static ListBinding<Item> Binding() => ListBinding<Item>.For(Definition, i => i.Id)
        .Column("name", i => i.Name)
        .Column("code", i => i.Code)
        .Column("quantity", i => i.Quantity)
        .Column("amount", i => i.Amount)
        .Column("active", i => i.Active)
        .Column("seen", i => i.Seen)
        .Column("day", i => i.Day)
        .Column("kind", i => i.Kind)
        .InMemory("test rows");

    private static readonly List<Item> Items = Enumerable.Range(0, 40).Select(i => new Item(
        Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
        $"Item {i % 7}",
        i % 5 == 0 ? null : $"C-{i:D3}",
        i % 9,
        (i % 4) * 2.5m,
        i % 3 != 0,
        i % 4 == 0 ? null : new DateTimeOffset(2026, 9, 1 + (i % 6), 8, 0, 0, TimeSpan.Zero),
        new DateOnly(2026, 1, 1 + (i % 3)),
        i % 2 == 0 ? "raw" : "finished")).ToList();

    private static HttpContext Http()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new StringCatalog([typeof(ErpPlatform).Assembly]));
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private static async Task<ListResult<Item>> Run(ListRequest request) =>
        await Binding().QueryAsync(Items.AsQueryable(), request, Http(), CancellationToken.None);

    [Theory]
    [InlineData("name eq 'x'")]
    [InlineData("(name eq 'it''s' or code is null) and not quantity ge 3")]
    [InlineData("kind in ('raw', 'finished') and seen ge '2026-09-02T00:00:00+04:00' and day le '2026-01-02'")]
    [InlineData("amount lt -1.5 and active ne false and code startswith 'C' and name endswith '1' and name contains 'em'")]
    public void Filters_parse_and_format_back_to_an_equivalent_text(string text)
    {
        var node = ListFilter.Parse(text);
        ListFilter.Validate(node, Definition);
        var formatted = ListFilter.Format(node);
        Assert.Equal(node, ListFilter.Parse(formatted), new NodeComparer());
    }

    [Theory]
    [InlineData("name eq", "list.filterSyntax")]
    [InlineData("name = 'x'", "list.filterSyntax")]
    [InlineData("name eq 'x' and", "list.filterSyntax")]
    [InlineData("((name eq 'x')", "list.filterSyntax")]
    [InlineData("name in ()", "list.filterSyntax")]
    [InlineData("name in ('a', null)", "list.filterSyntax")]
    [InlineData("name gt null", "list.filterSyntax")]
    [InlineData("1.2.3 eq 1", "list.filterSyntax")]
    [InlineData("nosuch eq 1", "list.unknownColumn")]
    [InlineData("name gt 'a'", "list.operatorNotAllowed")]
    [InlineData("active contains 'a'", "list.operatorNotAllowed")]
    [InlineData("quantity eq 'many'", "list.valueNumber")]
    [InlineData("active eq 1", "list.valueBoolean")]
    [InlineData("day eq '2026-13-01'", "list.valueDate")]
    [InlineData("seen eq '2026-09-01 08:00'", "list.valueDateTime")]
    [InlineData("kind eq 'other'", "list.valueChoice")]
    public void Bad_filters_name_the_problem(string text, string code)
    {
        var error = Assert.Throws<ListQueryException>(() => ListFilter.Validate(ListFilter.Parse(text), Definition));
        Assert.Equal(code, error.Code);
        Assert.Equal("filter", error.Parameter);
    }

    [Fact]
    public void Filters_are_bounded_in_length_and_complexity()
    {
        Assert.Equal("maxLength", Assert.Throws<ListQueryException>(() => ListFilter.Parse(new string('x', ListFilter.MaxLength + 1))).Code);
        var many = string.Join(" or ", Enumerable.Range(0, ListFilter.MaxConditions + 1).Select(i => $"quantity eq {i}"));
        Assert.Equal("list.filterTooComplex", Assert.Throws<ListQueryException>(() => ListFilter.Parse(many)).Code);
        var deep = new string('(', ListFilter.MaxDepth + 2) + "quantity eq 1" + new string(')', ListFilter.MaxDepth + 2);
        Assert.Equal("list.filterTooComplex", Assert.Throws<ListQueryException>(() => ListFilter.Parse(deep)).Code);
    }

    [Fact]
    public void Registration_refuses_definitions_and_bindings_that_cannot_keep_their_promises()
    {
        var bad = Definition with
        {
            Columns =
            [
                new ListColumn("name", "a", ListColumnType.Text, Sortable: true),
                new ListColumn("seen", "b", ListColumnType.DateTime, Groupable: true),
                new ListColumn("kind", "c", ListColumnType.Choice, Filterable: true),
                new ListColumn("label", "d", ListColumnType.Text, Aggregate: true),
                new ListColumn("Bad", "e", ListColumnType.Text),
            ],
            SearchFields = ["name"],
            DefaultSort = "-kind",
            Presets = [new ListPreset("p", "x", Filter: "name eq"), new ListPreset("q", "y", GroupBy: "name")],
        };
        var problems = bad.Problems("stock").ToList();
        Assert.Contains(problems, p => p.Contains("'seen' of type DateTime cannot be grouped", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("choice column 'kind' is filterable or groupable but lists no choices", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("'label' totals only number and money", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("column key 'Bad' must be lower camel case", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("default sort '-kind' is not a sortable column", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("preset 'p' filter is invalid", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("preset 'q' groups by 'name'", StringComparison.Ordinal));

        var binding = ListBinding<Item>.For(Definition, i => i.Id).Column("name", i => i.Quantity).Column("ghost", i => i.Name);
        var bindingProblems = binding.Problems().ToList();
        Assert.Contains(bindingProblems, p => p.Contains("column 'name' (Text) is bound to a Int32", StringComparison.Ordinal));
        Assert.Contains(bindingProblems, p => p.Contains("column 'code' is sortable, filterable, groupable, totalled or searched but not bound", StringComparison.Ordinal));
        Assert.Contains(bindingProblems, p => p.Contains("'ghost' is bound but is not a column", StringComparison.Ordinal));
        var host = Microsoft.AspNetCore.Builder.WebApplication.CreateSlimBuilder();
        host.Configuration["ConnectionStrings:App"] = "Host=localhost;Username=erp_app;Password=x;Database=erp";
        var refused = Assert.Throws<InvalidOperationException>(() => host.AddErpPlatform([new StockModule(binding)]));
        Assert.Contains("is bound to a Int32", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("-name")]
    [InlineData("code")]
    [InlineData("-code")]
    [InlineData("seen")]
    [InlineData("-seen")]
    [InlineData("active,-quantity")]
    [InlineData("-quantity,code")]
    public async Task Keyset_pages_return_every_row_once_in_the_order_of_one_page(string sort)
    {
        var whole = await Run(new ListRequest { Sort = sort, Take = 200 });
        Assert.Equal(Items.Count, whole.Rows.Count);
        Assert.Null(whole.Next);
        var walked = new List<Guid>();
        string? next = null;
        do
        {
            var page = await Run(new ListRequest { Sort = sort, Take = 3, After = next });
            Assert.Null(page.Problem);
            walked.AddRange(page.Rows.Select(r => r.Id));
            next = page.Next;
        }
        while (next is not null);
        Assert.Equal(whole.Rows.Select(r => r.Id), walked);
        var offset = new List<Guid>();
        for (var skip = 0; skip < Items.Count; skip += 7)
        {
            offset.AddRange((await Run(new ListRequest { Sort = sort, Take = 7, Skip = skip })).Rows.Select(r => r.Id));
        }
        Assert.Equal(walked, offset);
    }

    [Fact]
    public async Task Search_filters_and_groups_select_the_right_rows()
    {
        var search = await Run(new ListRequest { Search = "ITEM 3", Take = 200 });
        Assert.All(search.Rows, r => Assert.True(r.Name.Contains("item", StringComparison.OrdinalIgnoreCase) && (r.Name.Contains('3') || (r.Code ?? "").Contains('3'))));
        Assert.Equal(Items.Count(r => r.Name.Contains('3') || (r.Code ?? "").Contains('3')), search.Total);
        var wildcard = await Run(new ListRequest { Search = "%" });
        Assert.Equal(0, wildcard.Total);

        var filtered = await Run(new ListRequest { Filter = "(kind eq 'raw' or code is null) and quantity ge 4 and seen ge '2026-09-03'", Take = 200 });
        var expected = Items.Where(r => (r.Kind == "raw" || r.Code is null) && r.Quantity >= 4 && r.Seen >= new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero)).Select(r => r.Id).Order();
        Assert.Equal(expected, filtered.Rows.Select(r => r.Id).Order());
        var negated = await Run(new ListRequest { Filter = "code ne 'C-001'", Take = 200 });
        Assert.Equal(Items.Count - 1, negated.Total);

        var grouped = await Run(new ListRequest { GroupBy = "kind", Take = 1 });
        var groups = grouped.Groups!;
        Assert.Equal(["finished", "raw"], groups.Select(g => (string)g.Key!));
        Assert.Equal(Items.Count, groups.Sum(g => g.Count));
        Assert.Equal(Items.Sum(i => i.Amount), groups.Sum(g => g.Totals!["amount"]));
        Assert.Equal(Items.Sum(i => (decimal)i.Quantity), groups.Sum(g => g.Totals!["quantity"]));
        Assert.Equal(3, (await Run(new ListRequest { GroupBy = "day" })).Groups!.Count);
    }

    [Theory]
    [InlineData("sort", "kind", "list.notSortable")]
    [InlineData("sort", "-", "list.sortSyntax")]
    [InlineData("sort", "a,b,c,d,e", "list.sortTooMany")]
    [InlineData("groupBy", "name", "list.notGroupable")]
    [InlineData("after", "eyJzIjoibmFtZSJ9", "list.invalidCursor")]
    [InlineData("search", "a b c d e f g h i", "list.searchTooManyWords")]
    public async Task Bad_requests_become_400_problems_naming_the_parameter(string parameter, string value, string code)
    {
        var request = parameter switch
        {
            "sort" => new ListRequest { Sort = value },
            "groupBy" => new ListRequest { GroupBy = value },
            "after" => new ListRequest { After = value },
            _ => new ListRequest { Search = value },
        };
        var result = await Run(request);
        var problem = Assert.IsType<ProblemHttpResult>(result.Problem);
        Assert.Equal(400, problem.StatusCode);
        var errors = (IDictionary<string, Erp.Kernel.Http.FieldError[]>)problem.ProblemDetails.Extensions["errors"]!;
        Assert.Equal(code, errors[parameter][0].Code);
        // Only the list's own column keys are ever named back; nothing the caller typed is.
        if (Definition.Column(value) is null)
        {
            Assert.DoesNotContain(value, JsonSerializer.Serialize(problem.ProblemDetails), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("item 3")]
    [InlineData("c-0")]
    [InlineData("3")]
    [InlineData("item")]
    public async Task Keyset_pages_of_a_search_in_relevance_order_return_every_row_once(string search)
    {
        var whole = await Run(new ListRequest { Search = search, Take = 200 });
        Assert.Null(whole.Next);
        var walked = new List<Guid>();
        string? next = null;
        do
        {
            var page = await Run(new ListRequest { Search = search, Take = 2, After = next });
            Assert.Null(page.Problem);
            walked.AddRange(page.Rows.Select(r => r.Id));
            next = page.Next;
        }
        while (next is not null);
        Assert.Equal(whole.Total, walked.Count);
        Assert.Equal(whole.Rows.Select(r => r.Id), walked);
        // A cursor of the relevance order is refused for an explicit sort.
        var first = await Run(new ListRequest { Search = search, Take = 1 });
        if (first.Next is not null)
        {
            Assert.NotNull((await Run(new ListRequest { Search = search, Sort = "name", Take = 1, After = first.Next })).Problem);
        }
    }

    public sealed record Person(Guid Id, string Name, string? Email);

    private static readonly ListDefinition People = new(
        "crm.people", "crm.people.title", "crm.people.read", "/api/crm/people",
        [
            new ListColumn("name", "crm.people.name", ListColumnType.Text, Sortable: true, Filterable: true),
            new ListColumn("email", "crm.people.email", ListColumnType.Text, Filterable: true),
        ],
        SearchFields: ["name", "email"],
        DefaultSort: "name");

    private static async Task<IReadOnlyList<string>> Find(IEnumerable<string> names, string search, string? sort = null)
    {
        var rows = names.Select(n => new Person(Guid.CreateVersion7(), n, $"{n.Replace(' ', '.').ToLowerInvariant()}@example.test")).ToList();
        var binding = ListBinding<Person>.For(People, p => p.Id).Column("name", p => p.Name).Column("email", p => p.Email).InMemory("test rows");
        var result = await binding.QueryAsync(rows.AsQueryable(), new ListRequest { Search = search, Sort = sort, Take = 200 }, Http(), CancellationToken.None);
        Assert.Null(result.Problem);
        return result.Rows.Select(r => r.Name).ToList();
    }

    [Fact]
    public async Task A_search_without_a_sort_puts_the_best_match_first()
    {
        string[] names = ["Aisha Wang", "Wang Yousef", "Yousef Wangari", "Ali Yousefi", "Yousef Wang", "Mariam Yousef Wanless", "Yousef Al Wan"];
        // Both words at the start of name parts, and the whole search at the start of the name;
        // among equals the shorter (closer) name first.
        Assert.Equal(["Yousef Wang", "Yousef Wangari", "Yousef Al Wan", "Wang Yousef", "Mariam Yousef Wanless"], await Find(names, "yousef wan"));
        // The exact name beats every longer one.
        Assert.Equal("Yousef Wang", (await Find(names, "Yousef Wang"))[0]);
        // A prefix of a name part is enough.
        Assert.Equal("Yousef Wang", (await Find(names, "yous wang"))[0]);
        // An explicit sort is kept as asked.
        Assert.Equal(["Mariam Yousef Wanless", "Wang Yousef", "Yousef Al Wan", "Yousef Wang", "Yousef Wangari"], await Find(names, "yousef wan", "name"));
    }

    [Fact]
    public async Task A_search_too_broad_to_rank_keeps_the_default_order_on_every_page()
    {
        var rows = Enumerable.Range(0, ListSearch.MaxRankedRows + 50)
            .Select(i => new Person(Guid.CreateVersion7(), $"Person {i:D5}", $"p{i}@example.test")).ToList();
        var binding = ListBinding<Person>.For(People, p => p.Id).Column("name", p => p.Name).Column("email", p => p.Email).InMemory("test rows");
        async Task<ListResult<Person>> Run(ListRequest request) => await binding.QueryAsync(rows.AsQueryable(), request, Http(), CancellationToken.None);

        var broad = await Run(new ListRequest { Search = "person", Take = 200 });
        Assert.False(broad.Ranked);
        Assert.Equal(rows.Count, broad.Total);
        Assert.Equal(broad.Rows.Select(r => r.Name).Order(StringComparer.Ordinal), broad.Rows.Select(r => r.Name));
        // Its cursor continues in the same order.
        var second = await Run(new ListRequest { Search = "person", Take = 200, After = broad.Next });
        Assert.Null(second.Problem);
        Assert.False(second.Ranked);
        Assert.True(string.CompareOrdinal(broad.Rows[^1].Name, second.Rows[0].Name) < 0);

        var narrow = await Run(new ListRequest { Search = "person 00012", Take = 200 });
        Assert.True(narrow.Ranked);
        Assert.Equal("Person 00012", narrow.Rows[0].Name);
        Assert.False((await Run(new ListRequest { Search = "person 00012", Sort = "name" })).Ranked);
    }

    [Fact]
    public async Task Arabic_search_matches_the_spellings_people_type_for_one_another()
    {
        string[] names = ["فاطمة الزعابي", "أحمد المنصوري", "إبراهيم الكعبي", "مُحَمَّد علي", "ليلى الهاشمي"];
        Assert.Equal(["فاطمة الزعابي"], await Find(names, "فاطمه"));
        Assert.Equal(["فاطمة الزعابي"], await Find(names, "الزعابى"));
        Assert.Equal(["أحمد المنصوري"], await Find(names, "احمد"));
        Assert.Equal(["إبراهيم الكعبي"], await Find(names, "ابراهيم"));
        Assert.Equal(["ليلى الهاشمي"], await Find(names, "ليلي"));
        // Short vowels typed in the search are ignored (stored ones still have to be typed).
        Assert.Equal(["أحمد المنصوري"], await Find(names, "أَحْمَد"));
        // The whole name in other spellings is still the exact match, ahead of longer names.
        Assert.Equal("فاطمة الزعابي", (await Find([.. names, "فاطمة الزعابي الكبيرة"], "فاطمه الزعابى"))[0]);
        // "احمد" never matches "محمد": only the letter variants are interchangeable.
        Assert.DoesNotContain("مُحَمَّد علي", await Find(names, "احمد"));
    }

    [Fact]
    public void A_search_word_expands_to_a_bounded_set_of_spellings()
    {
        Assert.Equal(["wang"], ListSearch.Spellings("wang"));
        var fatima = ListSearch.Spellings("فاطمه");
        Assert.Equal("فاطمه", fatima[0]);
        Assert.Contains("فاطمة", fatima);
        Assert.Contains("فأطمة", fatima);
        Assert.Equal(10, fatima.Count);
        // One letter changed before two.
        Assert.True(fatima.ToList().IndexOf("فاطمة") < fatima.ToList().IndexOf("فأطمة"));
        var many = ListSearch.Spellings("ااااااااا");
        Assert.InRange(many.Count, 2, ListSearch.MaxSpellings);
        Assert.Equal("ااااااااا", many[0]);
    }

    [Fact]
    public async Task A_cursor_belongs_to_one_sort()
    {
        var first = await Run(new ListRequest { Sort = "name", Take = 5 });
        var other = await Run(new ListRequest { Sort = "-name", Take = 5, After = first.Next });
        Assert.NotNull(other.Problem);
        var both = await Run(new ListRequest { Sort = "name", Take = 5, After = first.Next, Skip = 5 });
        Assert.NotNull(both.Problem);
    }

    private sealed class NodeComparer : IEqualityComparer<FilterNode>
    {
        public bool Equals(FilterNode? x, FilterNode? y) => ListFilter.Format(x!) == ListFilter.Format(y!);

        public int GetHashCode(FilterNode obj) => ListFilter.Format(obj).GetHashCode(StringComparison.Ordinal);
    }

    private sealed class StockModule(ListBinding<Item> binding) : ErpModule
    {
        public override string Name => "stock";

        public override void Register(ModuleBuilder module)
        {
            module.Permissions("stock.items.read");
            module.List(binding);
        }
    }
}
