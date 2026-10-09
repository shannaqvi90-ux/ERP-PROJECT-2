using Erp.Kernel.Hosting;
using Erp.Kernel.Lists;
using Erp.Kernel.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Kernel.Tests;

/// <summary>Initials (<see cref="ListBinding{T}.Initials"/>): a one-word search also finds the rows
/// whose name's words start with its letters ("map" for Majid Anil Pillai), below every row the word itself
/// matches, so a name that is also someone's initials keeps its people first. Lists without
/// initials, searches of more than one word, words with digits and Arabic words never try them.</summary>
public sealed class ListInitialsSearchTests
{
    public sealed class Person
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }

    private static readonly ListDefinition People = new(
        "crm.people", "crm.people.title", "crm.people.read", "/api/crm/people",
        [
            new ListColumn("name", "crm.people.name", ListColumnType.Text, Sortable: true, Filterable: true),
            new ListColumn("email", "crm.people.email", ListColumnType.Text, Filterable: true),
        ],
        SearchFields: ["name", "email"],
        DefaultSort: "name");

    private static ListBinding<Person> Plain() => ListBinding<Person>.For(People, p => p.Id).Column("name", p => p.Name).Column("email", p => p.Email);

    private static Person Row(string name, string email) => new() { Id = Guid.CreateVersion7(), Name = name, Email = email };

    private static readonly List<Person> Rows =
    [
        Row("Majid Anil Pillai", "majid.pillai@staff.example"),
        Row("Mona Ali Patel", "mona.patel@staff.example"),
        Row("Mapara Khan", "m.khan@staff.example"),
        Row("Ali Hassan", "ali.hassan@staff.example"),
        Row("Ahmed Latif Ibrahim", "a.ibrahim@staff.example"),
        Row("Sara Noor", "sara@staff.example"),
        Row("Noor Al-Saadi", "n.saadi@staff.example"),
    ];

    private static HttpContext Http()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new StringCatalog([typeof(ErpPlatform).Assembly]));
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private static async Task<IReadOnlyList<string>> Find(string search, ListBinding<Person>? binding = null)
    {
        var result = await (binding ?? Plain().Initials(p => p.Name)).InMemory("test rows")
            .QueryAsync(Rows.AsQueryable(), new ListRequest { Search = search, Take = 200 }, Http(), CancellationToken.None);
        Assert.Null(result.Problem);
        return result.Rows.Select(r => r.Name).ToList();
    }

    [Fact]
    public async Task A_word_equal_to_someones_initials_finds_them_after_the_people_the_word_itself_matches()
    {
        // "map": the word starts a name (Mapara Khan), then the initials, the shorter name first.
        Assert.Equal(["Mapara Khan", "Mona Ali Patel", "Majid Anil Pillai"], await Find("map"));
        Assert.Equal(["Mapara Khan", "Mona Ali Patel", "Majid Anil Pillai"], await Find("MAP"));
        // "ali": the people named Ali (and Mona Ali Patel) first, then the initials of Ahmed Latif Ibrahim.
        var ali = (await Find("ali")).ToList();
        Assert.Equal("Ahmed Latif Ibrahim", ali[^1]);
        Assert.Contains("Ali Hassan", ali);
        Assert.Contains("Mona Ali Patel", ali);
        Assert.True(ali.IndexOf("Ali Hassan") < ali.IndexOf("Ahmed Latif Ibrahim"));
        Assert.Equal(["Sara Noor"], await Find("sn"));
    }

    [Fact]
    public async Task Initials_are_tried_only_for_one_word_of_letters_on_a_list_that_has_them()
    {
        Assert.Equal(["Mapara Khan"], await Find("map", Plain()));
        Assert.Empty(await Find("map zz"));
        Assert.Empty(await Find("sn1"));
        // One letter is no initials (a name of one word would match anything typed).
        Assert.Equal(await Find("s", Plain()), await Find("s"));
        Assert.Empty(await Find("مأب"));
    }

    [Fact]
    public async Task Initials_need_one_word_per_letter_in_order_and_words_end_at_hyphens()
    {
        // Majid Anil Pillai has three words; "ma" (two) and "mapk" (four) are not his initials.
        Assert.DoesNotContain("Majid Anil Pillai", await Find("mapk"));
        Assert.DoesNotContain("Majid Anil Pillai", await Find("pam"));
        Assert.Equal(["Mapara Khan"], await Find("mk"));
        Assert.Contains("Ahmed Latif Ibrahim", await Find("ALI"));
        Assert.Equal("^m[^ -]*[ -]+a[^ -]*[ -]+p[^ -]*$", ListSearch.InitialsPattern("map"));
        // Words end at hyphens too.
        Assert.Equal(["Noor Al-Saadi"], await Find("nas"));
    }
}
