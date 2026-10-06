using Erp.Kernel.Hosting;
using Erp.Kernel.Lists;
using Erp.Kernel.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Kernel.Tests;

/// <summary>A search field of Arabic script (a name in Arabic beside a Latin one): Arabic words
/// find rows through it, Latin words never try it, so a list gains the field without making
/// every Latin search wider.</summary>
public sealed class ListScriptSearchTests
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
            new ListColumn("nameAr", "crm.people.nameAr", ListColumnType.Text, Filterable: true, Script: ListTextScript.Arabic),
        ],
        SearchFields: ["name", "email", "nameAr"],
        DefaultSort: "name");

    private static ListBinding<Person> Binding() => ListBinding<Person>.For(People, p => p.Id)
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

    private static async Task<IReadOnlyList<string>> Find(string search)
    {
        var result = await Binding().InMemory("test rows").QueryAsync(Rows.AsQueryable(), new ListRequest { Search = search, Take = 200 }, Http(), CancellationToken.None);
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
    public void A_Latin_search_queries_the_same_fields_as_before_and_an_Arabic_one_adds_the_Arabic_field()
    {
        using var db = new PeopleDb();
        // The conditions and the order (relevance), without the selected columns.
        string Sql(string search)
        {
            var sql = Binding().Apply(db.People, new ListRequest { Search = search }).ToQueryString();
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
    }

    [Fact]
    public void A_script_is_named_only_for_text_and_one_search_field_stays_open_to_every_word()
    {
        var onlyArabic = People with
        {
            Columns = [new ListColumn("nameAr", "crm.people.nameAr", ListColumnType.Text, Sortable: true, Script: ListTextScript.Arabic)],
            SearchFields = ["nameAr"],
            DefaultSort = "nameAr",
        };
        Assert.Contains(onlyArabic.Problems("crm"), p => p.Contains("every search field is limited to one script", StringComparison.Ordinal));
        var numberWithScript = People with
        {
            Columns = [.. People.Columns, new ListColumn("count", "crm.people.count", ListColumnType.Number, Script: ListTextScript.Arabic)],
        };
        Assert.Contains(numberWithScript.Problems("crm"), p => p.Contains("names a script but is not a text column", StringComparison.Ordinal));
        Assert.Empty(People.Problems("crm"));
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
