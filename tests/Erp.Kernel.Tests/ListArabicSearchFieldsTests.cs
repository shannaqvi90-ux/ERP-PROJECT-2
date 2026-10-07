using Erp.Kernel.Hosting;
using Erp.Kernel.Lists;
using Erp.Kernel.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Kernel.Tests;

/// <summary>Arabic search fields (<see cref="ListDefinition.ArabicSearchFields"/>, a name in Arabic
/// beside a Latin one): words written in Arabic letters find rows through them, Latin words never
/// try them, so a list gains the Arabic name without making every Latin search wider.</summary>
public sealed class ListArabicSearchFieldsTests
{
    public sealed class Person
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string? NameAr { get; set; }
    }

    private static readonly ListDefinition People = new(
        "crm.people", "crm.people.title", "crm.people.read", "/api/crm/people",
        [
            new ListColumn("name", "crm.people.name", ListColumnType.Text, Sortable: true, Filterable: true),
            new ListColumn("email", "crm.people.email", ListColumnType.Text, Filterable: true),
            new ListColumn("nameAr", "crm.people.nameAr", ListColumnType.Text, Filterable: true),
        ],
        SearchFields: ["name", "email"],
        DefaultSort: "name",
        // This list keeps the address for Arabic words (internationalised addresses).
        ArabicSearchFields: ["name", "email", "nameAr"]);

    /// <summary>The same list as the users list shapes it: Arabic words search the two names only.</summary>
    private static readonly ListDefinition NamesOnly = People with { ArabicSearchFields = ["name", "nameAr"] };

    private static ListBinding<Person> Binding(ListDefinition? definition = null) => ListBinding<Person>.For(definition ?? People, p => p.Id)
        .Column("name", p => p.Name).Column("email", p => p.Email).Column("nameAr", p => p.NameAr);

    private static readonly List<Person> Rows =
    [
        new() { Id = Guid.CreateVersion7(), Name = "Fatima Al Zaabi", Email = "fatima@alnoor.example", NameAr = "فاطمة الزعابي" },
        new() { Id = Guid.CreateVersion7(), Name = "Fatima Al Zaabi Senior", Email = "fz@alnoor.example", NameAr = "فاطمة الزعابي الكبيرة" },
        new() { Id = Guid.CreateVersion7(), Name = "Omar Haddad", Email = "omar@alnoor.example", NameAr = "عمر حداد" },
        // An Arabic name typed into the main name field is still found by Arabic words.
        new() { Id = Guid.CreateVersion7(), Name = "مريم الكعبي", Email = "mariam@alnoor.example", NameAr = null },
        // Latin text in the Arabic field is not what Latin words search (the Latin name is).
        new() { Id = Guid.CreateVersion7(), Name = "Layla Hashimi", Email = "layla@alnoor.example", NameAr = "zzqq" },
        // An internationalised address in Arabic script is still searched by Arabic words.
        new() { Id = Guid.CreateVersion7(), Name = "Hassan Ali", Email = "حسن@مثال.امارات", NameAr = null },
    ];

    private static HttpContext Http()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new StringCatalog([typeof(ErpPlatform).Assembly]));
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private static async Task<IReadOnlyList<string>> Find(string search, ListDefinition? definition = null)
    {
        var result = await Binding(definition).InMemory("test rows").QueryAsync(Rows.AsQueryable(), new ListRequest { Search = search, Take = 200 }, Http(), CancellationToken.None);
        Assert.Null(result.Problem);
        return result.Rows.Select(r => r.Name).ToList();
    }

    [Fact]
    public async Task Arabic_words_find_people_by_their_Arabic_name_in_the_spellings_people_type()
    {
        Assert.Equal(["Fatima Al Zaabi", "Fatima Al Zaabi Senior"], await Find("فاطمه الزعابى"));
        Assert.Equal("Fatima Al Zaabi", (await Find("فاطمة الزعابي"))[0]);
        Assert.Equal(["Omar Haddad"], await Find("حداد"));
        Assert.Equal(["مريم الكعبي"], await Find("الكعبى"));
        Assert.Equal(["Hassan Ali"], await Find("حسن"));
        // Latin and Arabic words in one search: each word in a field it can occur in.
        Assert.Equal(["Omar Haddad"], await Find("omar حداد"));
    }

    [Fact]
    public async Task Latin_words_never_search_the_Arabic_field()
    {
        Assert.Empty(await Find("zzqq"));
        Assert.Equal(["Layla Hashimi"], await Find("layla"));
        Assert.Equal(["Fatima Al Zaabi", "Fatima Al Zaabi Senior"], await Find("fatima"));
    }

    [Fact]
    public async Task Arabic_words_search_only_the_Arabic_search_fields_and_Latin_words_only_the_search_fields()
    {
        // The users list's shape: an Arabic word no longer looks in the address.
        Assert.Empty(await Find("حسن", NamesOnly));
        Assert.Equal(["Omar Haddad"], await Find("حداد", NamesOnly));
        Assert.Equal(["مريم الكعبي"], await Find("مريم", NamesOnly));
        Assert.Empty(await Find("zzqq", NamesOnly));
        // A list that names no Arabic search fields searches its search fields with every word.
        var noArabic = People with { ArabicSearchFields = null };
        Assert.Equal(["Hassan Ali"], await Find("حسن", noArabic));
        Assert.Equal(["Layla Hashimi"], await Find("zzqq", noArabic with { SearchFields = ["name", "email", "nameAr"] }));
    }

    [Theory]
    [InlineData("فاطمه", new[] { "name", "nameAr" })]
    [InlineData("ﻓﺎﻃﻤﺔ", new[] { "name", "nameAr" })]
    [InlineData("x@مثال", new[] { "name", "nameAr" })]
    [InlineData("omar", new[] { "name", "email" })]
    [InlineData("١٢٣", new[] { "name", "email" })]
    [InlineData("o'neil-2", new[] { "name", "email" })]
    public void A_word_matches_the_Arabic_search_fields_when_it_has_an_Arabic_letter(string word, string[] fields)
    {
        Assert.Equal(fields, NamesOnly.SearchFieldsFor(word));
        Assert.Equal(["name", "email", "nameAr"], NamesOnly.AllSearchFields);
        Assert.Equal(["name", "email"], (People with { ArabicSearchFields = null }).SearchFieldsFor(word));
    }

    [Fact]
    public void A_Latin_search_queries_the_same_fields_as_before_and_an_Arabic_one_adds_the_Arabic_field()
    {
        using var db = new PeopleDb();
        // The conditions and the order (relevance), without the selected columns.
        string Sql(string search, ListDefinition? definition = null)
        {
            var sql = Binding(definition).Apply(db.People, new ListRequest { Search = search }).ToQueryString();
            return sql[sql.IndexOf("FROM people", StringComparison.Ordinal)..];
        }

        var latin = Sql("omar had");
        Assert.DoesNotContain("name_ar", latin, StringComparison.Ordinal);
        Assert.Contains("email", latin, StringComparison.Ordinal);

        var arabic = Sql("فاطمه");
        Assert.Contains("name_ar", arabic, StringComparison.Ordinal);
        Assert.Contains("email", arabic, StringComparison.Ordinal);

        // The conditions alone (relevance orders with regular expressions in either case).
        static string Where(string sql) => sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..sql.IndexOf("ORDER BY", StringComparison.Ordinal)];
        // A Latin word: one LIKE per field, no regular expression.
        Assert.DoesNotContain("~*", Where(latin), StringComparison.Ordinal);
        // An Arabic word (many spellings): one regular expression per field it can occur in, which
        // refuses most rows before the LIKE patterns (one per spelling and field) are tried.
        var where = Where(arabic);
        Assert.Equal(3, where.Split("~*").Length - 1);
        Assert.Equal(3 * ListSearch.Spellings("فاطمه").Count, where.Split("ILIKE").Length - 1);
        Assert.True(where.IndexOf("~*", StringComparison.Ordinal) < where.IndexOf("ILIKE", StringComparison.Ordinal), where);

        // The users list's shape: an Arabic word tries two fields, as many as a Latin word.
        var namesOnly = Where(Sql("فاطمه", NamesOnly));
        Assert.DoesNotContain("email", namesOnly, StringComparison.Ordinal);
        Assert.Equal(2, namesOnly.Split("~*").Length - 1);
        Assert.Equal(2 * ListSearch.Spellings("فاطمه").Count, namesOnly.Split("ILIKE").Length - 1);
        // Mixed words: each word in its own fields; the whole-search ranking only in the field both share.
        var mixed = Sql("omar حداد", NamesOnly);
        Assert.Contains("name_ar", Where(mixed), StringComparison.Ordinal);
        Assert.Contains("email", Where(mixed), StringComparison.Ordinal);
    }

    [Fact]
    public void Arabic_search_fields_are_text_columns_and_are_bound()
    {
        var notAColumn = People with { ArabicSearchFields = ["name", "nameFr"] };
        Assert.Contains(notAColumn.Problems("crm"), p => p.Contains("search field 'nameFr' is not a column", StringComparison.Ordinal));
        var number = People with
        {
            Columns = [.. People.Columns, new ListColumn("count", "crm.people.count", ListColumnType.Number)],
            ArabicSearchFields = ["name", "count"],
        };
        Assert.Contains(number.Problems("crm"), p => p.Contains("search field 'count' is not a text column", StringComparison.Ordinal));
        Assert.Empty(People.Problems("crm"));
        Assert.Empty(NamesOnly.Problems("crm"));
        // An Arabic search field without a binding is reported at start-up like any search field.
        var unbound = ListBinding<Person>.For(People, p => p.Id).Column("name", p => p.Name).Column("email", p => p.Email);
        Assert.Contains(unbound.Problems(), p => p.Contains("'nameAr'", StringComparison.Ordinal));
        Assert.Empty(Binding().Problems());
    }

    [Theory]
    [InlineData("فاطمه", true)]
    [InlineData("omar", false)]
    [InlineData("o'neil-2", false)]
    [InlineData("١٢٣", false)]
    [InlineData("ﻓﺎﻃﻤﺔ", true)]
    [InlineData("x@مثال", true)]
    public void A_word_has_an_Arabic_letter_when_it_contains_one(string word, bool arabic) =>
        Assert.Equal(arabic, ListSearch.HasArabicLetter(word));

    private sealed class PeopleDb : DbContext
    {
        public DbSet<Person> People => Set<Person>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseNpgsql("Host=unused;Database=unused");

        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<Person>(e =>
            {
                e.ToTable("people");
                e.Property(p => p.Id).HasColumnName("id");
                e.Property(p => p.Name).HasColumnName("name");
                e.Property(p => p.Email).HasColumnName("email");
                e.Property(p => p.NameAr).HasColumnName("name_ar");
            });
    }
}
