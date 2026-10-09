using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

/// <summary>Its own environment: these tests change sign-in times and add users.</summary>
public sealed class UsersListFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Env = await ErpTestEnvironment.StartGateAsync();
        // Mixed sign-in times, some equal, the rest never: keyset paging must cross ties and nulls.
        await using var admin = await Env.OpenAdminAsync();
        await using var command = new Npgsql.NpgsqlCommand("""
            UPDATE identity.users SET last_sign_in_at = timestamptz '2026-09-01 08:00:00+00' + (((row_number - 1) % 4) * interval '1 hour')
              FROM (SELECT id AS uid, row_number() OVER (ORDER BY id) AS row_number FROM identity.users WHERE tenant_id = @t) n
             WHERE id = n.uid AND n.row_number % 3 <> 0
            """, admin);
        command.Parameters.AddWithValue("t", Env.TenantA.Id);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// The list query contract on the users list (database) and the roles list (in memory): search
/// word by word, the filter language, sort both ways with nulls and ties, keyset paging that
/// matches offset paging row for row, grouping, and 400 answers that name the parameter.
/// </summary>
public sealed class UsersListTests(UsersListFixture fixture) : IClassFixture<UsersListFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static async Task<JsonElement> Get(HttpClient client, string uri)
    {
        using var response = await client.GetAsync(uri);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{uri}: {(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> Problem(HttpClient client, string uri)
    {
        using var response = await client.GetAsync(uri);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{uri}: expected 400, got {(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string Q(string value) => Uri.EscapeDataString(value);

    private static List<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray().ToList();

    private static List<string> Ids(JsonElement page) => Items(page).Select(i => i.GetProperty("id").GetString()!).ToList();

    [Theory]
    [InlineData("-createdAt")]
    [InlineData("createdAt")]
    [InlineData("displayName")]
    [InlineData("-displayName")]
    [InlineData("email")]
    [InlineData("lastSignInAt")]
    [InlineData("-lastSignInAt")]
    [InlineData("lastSignInAt,-displayName")]
    public async Task Keyset_paging_returns_the_same_rows_in_the_same_order_as_one_page(string sort)
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var whole = await Get(admin, $"/api/identity/users?sort={Q(sort)}&take=200");
        var expected = Ids(whole);
        Assert.True(expected.Count > 20, $"only {expected.Count} users");
        Assert.Equal(JsonValueKind.Null, whole.GetProperty("next").ValueKind);

        var walked = new List<string>();
        string? next = null;
        var pages = 0;
        do
        {
            var page = await Get(admin, $"/api/identity/users?sort={Q(sort)}&take=4" + (next is null ? "" : $"&after={Q(next)}"));
            Assert.Equal(expected.Count, page.GetProperty("total").GetInt32());
            walked.AddRange(Ids(page));
            next = page.GetProperty("next").ValueKind == JsonValueKind.Null ? null : page.GetProperty("next").GetString();
            pages++;
            Assert.True(pages < 100, "paging does not end");
        }
        while (next is not null);
        Assert.Equal(expected, walked);

        // Offset paging agrees too.
        var offset = new List<string>();
        for (var skip = 0; skip < expected.Count; skip += 6)
        {
            offset.AddRange(Ids(await Get(admin, $"/api/identity/users?sort={Q(sort)}&take=6&skip={skip}")));
        }
        Assert.Equal(expected, offset);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("om")]
    [InlineData("al m")]
    [InlineData("haddad omar")]
    [InlineData("فاطمه")]
    public async Task A_search_in_relevance_order_pages_every_row_once_in_the_order_of_one_page(string search)
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var whole = await Get(admin, $"/api/identity/users?search={Q(search)}&take=200");
        var expected = Ids(whole);
        Assert.True(expected.Count > 0, $"nothing found for {search}");
        var walked = new List<string>();
        string? next = null;
        var pages = 0;
        do
        {
            var page = await Get(admin, $"/api/identity/users?search={Q(search)}&take=3" + (next is null ? "" : $"&after={Q(next)}"));
            Assert.Equal(expected.Count, page.GetProperty("total").GetInt32());
            walked.AddRange(Ids(page));
            next = page.GetProperty("next").ValueKind == JsonValueKind.Null ? null : page.GetProperty("next").GetString();
            Assert.True(++pages < 200, "paging does not end");
        }
        while (next is not null);
        Assert.Equal(expected, walked);
        var offset = new List<string>();
        for (var skip = 0; skip < expected.Count; skip += 5)
        {
            offset.AddRange(Ids(await Get(admin, $"/api/identity/users?search={Q(search)}&take=5&skip={skip}")));
        }
        Assert.Equal(expected, offset);
    }

    [Fact]
    public async Task A_search_lists_the_best_match_first_and_reads_Arabic_spelling_variants()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        // The viewer is "Omar Haddad …": the words at the start of the name, in the typed order.
        var top = Items(await Get(admin, $"/api/identity/users?search={Q("omar had")}"))[0];
        Assert.Equal(Env.Email(Env.TenantA, "viewer"), top.GetProperty("email").GetString());
        // Names whose parts start with the words come before names that only contain them.
        static bool AtWordStart(string text) =>
            text.StartsWith("al", StringComparison.OrdinalIgnoreCase) || text.Contains(" al", StringComparison.OrdinalIgnoreCase) || text.Contains(".al", StringComparison.OrdinalIgnoreCase);
        var startsAt = Items(await Get(admin, $"/api/identity/users?search={Q("al")}&take=200"))
            .Select(i => AtWordStart(i.GetProperty("displayName").GetString()!) || AtWordStart(i.GetProperty("email").GetString()!)).ToList();
        Assert.Contains(true, startsAt);
        Assert.Contains(false, startsAt);
        Assert.True(startsAt.LastIndexOf(true) < startsAt.IndexOf(false), "every user with a word starting 'al' comes before the users that only contain 'al'");
        // An explicit sort is kept.
        var sorted = Items(await Get(admin, $"/api/identity/users?search={Q("al")}&sort=displayName&take=200")).Select(i => i.GetProperty("displayName").GetString()!).ToList();
        Assert.Equal(sorted.Order(StringComparer.Ordinal), sorted);
        // The administrator "فاطمة الزعابي" is found with heh for teh marbuta and alef maqsura for yeh.
        foreach (var spelling in new[] { "فاطمه", "الزعابى", "فاطمه الزعابى", "فاطِمة" })
        {
            var found = Items(await Get(admin, $"/api/identity/users?search={Q(spelling)}"));
            Assert.Contains(found, u => u.GetProperty("email").GetString() == Env.Email(Env.TenantA, "admin.ar"));
        }
    }

    [Fact]
    public async Task Sorting_orders_by_the_column_with_nulls_last_ascending_and_first_descending()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var ascending = Items(await Get(admin, "/api/identity/users?sort=lastSignInAt&take=200"))
            .Select(i => i.GetProperty("lastSignInAt")).ToList();
        var firstNull = ascending.FindIndex(v => v.ValueKind == JsonValueKind.Null);
        Assert.True(firstNull > 0, "some users have signed in and some have not");
        Assert.All(ascending.Skip(firstNull), v => Assert.Equal(JsonValueKind.Null, v.ValueKind));
        var times = ascending.Take(firstNull).Select(v => v.GetDateTimeOffset()).ToList();
        Assert.Equal(times.Order(), times);

        var descending = Items(await Get(admin, "/api/identity/users?sort=-lastSignInAt&take=200"))
            .Select(i => i.GetProperty("lastSignInAt")).ToList();
        var firstValue = descending.FindIndex(v => v.ValueKind != JsonValueKind.Null);
        Assert.True(firstValue > 0);
        var down = descending.Skip(firstValue).Select(v => v.GetDateTimeOffset()).ToList();
        Assert.Equal(down.OrderDescending(), down);

        var names = Items(await Get(admin, "/api/identity/users?sort=displayName&take=200")).Select(i => i.GetProperty("displayName").GetString()!).ToList();
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        var emails = Items(await Get(admin, "/api/identity/users?sort=-email&take=200")).Select(i => i.GetProperty("email").GetString()!.ToLowerInvariant()).ToList();
        Assert.Equal(emails.OrderDescending(StringComparer.Ordinal), emails);
    }

    [Fact]
    public async Task Search_matches_every_word_in_any_search_field_ignoring_case_and_order()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        foreach (var search in new[] { "omar haddad", "HADDAD omar", "haddad viewer@", "Omar" })
        {
            var page = await Get(admin, $"/api/identity/users?search={Q(search)}");
            Assert.Contains(Items(page), u => u.GetProperty("email").GetString() == Env.Email(Env.TenantA, "viewer"));
            Assert.All(Items(page), u =>
            {
                var text = (u.GetProperty("displayName").GetString() + " " + u.GetProperty("email").GetString()).ToLowerInvariant();
                Assert.All(search.ToLowerInvariant().Split(' '), word => Assert.Contains(word, text));
            });
        }
        Assert.Equal(0, (await Get(admin, $"/api/identity/users?search={Q("omar zzzqqq")}")).GetProperty("total").GetInt32());
        // LIKE wildcards are literal characters in a search.
        Assert.Equal(0, (await Get(admin, $"/api/identity/users?search={Q("%")}")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Get(admin, $"/api/identity/users?search={Q("o_ar")}")).GetProperty("total").GetInt32());
        var tooMany = await Problem(admin, $"/api/identity/users?search={Q("a b c d e f g h i")}");
        Assert.Equal("list.searchTooManyWords", tooMany.GetProperty("errors").GetProperty("search")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Search_finds_a_name_written_in_Arabic()
    {
        // Critic p03 round 3: searching "عمر حداد" found nobody although Arabic screens show it.
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        foreach (var search in new[] { "عمر حداد", "حداد", "حداد عمر", "omar حداد" })
        {
            var page = await Get(admin, $"/api/identity/users?search={Q(search)}");
            Assert.Contains(Items(page), u => u.GetProperty("email").GetString() == Env.Email(Env.TenantA, "viewer"));
        }
        // Every Arabic word must match: one that matches nobody finds nobody.
        Assert.Equal(0, (await Get(admin, $"/api/identity/users?search={Q("حداد زززق")}")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Filters_select_exactly_the_matching_rows()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var all = Items(await Get(admin, "/api/identity/users?take=200"));

        async Task Check(string filter, Func<JsonElement, bool> expected)
        {
            var page = await Get(admin, $"/api/identity/users?take=200&filter={Q(filter)}");
            var want = all.Where(expected).Select(u => u.GetProperty("id").GetString()).Order().ToList();
            var got = Ids(page).Order().ToList();
            Assert.True(want.SequenceEqual(got), $"{filter}: expected {want.Count} rows, got {got.Count}");
            Assert.Equal(want.Count, page.GetProperty("total").GetInt32());
        }

        static string Text(JsonElement u, string p) => u.GetProperty(p).GetString()!;
        var viewer = Env.Email(Env.TenantA, "viewer");
        var cut = all.Select(u => u.GetProperty("createdAt").GetDateTimeOffset()).Order().ElementAt(10);

        await Check("language eq 'ar'", u => Text(u, "language") == "ar");
        await Check("language ne 'ar'", u => Text(u, "language") != "ar");
        await Check("language in ('ar', 'en')", _ => true);
        await Check("isActive eq false", u => !u.GetProperty("isActive").GetBoolean());
        await Check("isActive eq true and language eq 'en'", u => u.GetProperty("isActive").GetBoolean() && Text(u, "language") == "en");
        await Check("lastSignInAt is null", u => u.GetProperty("lastSignInAt").ValueKind == JsonValueKind.Null);
        await Check("lastSignInAt is not null", u => u.GetProperty("lastSignInAt").ValueKind != JsonValueKind.Null);
        await Check("lastSignInAt eq null or language eq 'ar'", u => u.GetProperty("lastSignInAt").ValueKind == JsonValueKind.Null || Text(u, "language") == "ar");
        await Check("lastSignInAt ge '2026-09-01T09:00:00Z'", u => u.GetProperty("lastSignInAt") is { ValueKind: JsonValueKind.String } t && t.GetDateTimeOffset() >= new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero));
        await Check("lastSignInAt lt '2026-09-01T13:00:00+04:00'", u => u.GetProperty("lastSignInAt") is { ValueKind: JsonValueKind.String } t && t.GetDateTimeOffset() < new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero));
        await Check($"createdAt le '{cut.UtcDateTime:yyyy-MM-ddTHH:mm:ss.ffffffZ}'", u => u.GetProperty("createdAt").GetDateTimeOffset() <= cut);
        await Check("displayName contains 'HADDAD'", u => Text(u, "displayName").Contains("haddad", StringComparison.OrdinalIgnoreCase));
        await Check("not (displayName contains 'haddad')", u => !Text(u, "displayName").Contains("haddad", StringComparison.OrdinalIgnoreCase));
        await Check($"email eq '{viewer.ToUpperInvariant()}'", u => Text(u, "email") == viewer);
        await Check("email startswith 'ADMIN'", u => Text(u, "email").StartsWith("admin", StringComparison.OrdinalIgnoreCase));
        await Check("email endswith 'example'", u => Text(u, "email").EndsWith("example", StringComparison.OrdinalIgnoreCase));
        await Check("(language eq 'ar' or isActive eq false) and not email contains 'admin'",
            u => (Text(u, "language") == "ar" || !u.GetProperty("isActive").GetBoolean()) && !Text(u, "email").Contains("admin", StringComparison.OrdinalIgnoreCase));
        await Check("displayName eq 'it''s nobody'", _ => false);
    }

    [Theory]
    [InlineData("filter", "language eq", "list.filterSyntax")]
    [InlineData("filter", "language eq 'ar' and", "list.filterSyntax")]
    [InlineData("filter", "(language eq 'ar'", "list.filterSyntax")]
    [InlineData("filter", "language eq 'ar", "list.filterSyntax")]
    [InlineData("filter", "language = 'ar'", "list.filterSyntax")]
    [InlineData("filter", "nosuch eq 'x'", "list.unknownColumn")]
    [InlineData("filter", "language gt 'ar'", "list.operatorNotAllowed")]
    [InlineData("filter", "language eq 'fr'", "list.valueChoice")]
    [InlineData("filter", "isActive eq 'yes'", "list.valueBoolean")]
    [InlineData("filter", "createdAt ge 'yesterday'", "list.valueDateTime")]
    [InlineData("filter", "displayName eq 5", "list.valueText")]
    [InlineData("filter", "isActive gt true", "list.operatorNotAllowed")]
    [InlineData("sort", "nosuch", "list.unknownColumn")]
    [InlineData("sort", "language", "list.notSortable")]
    [InlineData("sort", "displayName,displayName", "list.sortSyntax")]
    [InlineData("sort", "display name", "list.sortSyntax")]
    [InlineData("groupBy", "displayName", "list.notGroupable")]
    [InlineData("groupBy", "nosuch", "list.unknownColumn")]
    [InlineData("after", "not-a-cursor", "list.invalidCursor")]
    public async Task Bad_queries_are_refused_naming_the_parameter(string parameter, string value, string code)
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin.ar"));
        var problem = await Problem(admin, $"/api/identity/users?{parameter}={Q(value)}");
        Assert.Equal("validation", problem.GetProperty("code").GetString());
        var error = problem.GetProperty("errors").GetProperty(parameter)[0];
        Assert.Equal(code, error.GetProperty("code").GetString());
        // The message is in the user's language and never repeats what was sent.
        Assert.Matches(@"\p{IsArabic}", error.GetProperty("message").GetString()!);
        string[] columns = ["displayName", "email", "language", "isActive", "lastSignInAt", "createdAt"];
        if (!columns.Contains(value))
        {
            Assert.DoesNotContain(value, problem.GetRawText(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_cursor_belongs_to_its_sort_and_cannot_be_combined_with_skip()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var page = await Get(admin, "/api/identity/users?sort=displayName&take=3");
        var next = page.GetProperty("next").GetString()!;
        var otherSort = await Problem(admin, $"/api/identity/users?sort=email&take=3&after={Q(next)}");
        Assert.Equal("list.invalidCursor", otherSort.GetProperty("errors").GetProperty("after")[0].GetProperty("code").GetString());
        var withSkip = await Problem(admin, $"/api/identity/users?sort=displayName&take=3&skip=3&after={Q(next)}");
        Assert.Equal("list.afterWithSkip", withSkip.GetProperty("errors").GetProperty("after")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Grouping_counts_every_matching_row_once()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        foreach (var query in new[] { "groupBy=language", "groupBy=isActive", $"groupBy=language&filter={Q("isActive eq true")}" })
        {
            var page = await Get(admin, $"/api/identity/users?{query}&take=1");
            var groups = page.GetProperty("groups").EnumerateArray().ToList();
            Assert.NotEmpty(groups);
            Assert.Equal(page.GetProperty("total").GetInt32(), groups.Sum(g => g.GetProperty("count").GetInt32()));
            Assert.Equal(groups.Count, groups.Select(g => g.GetProperty("key").ToString()).Distinct().Count());
        }
        var byLanguage = (await Get(admin, "/api/identity/users?groupBy=language")).GetProperty("groups").EnumerateArray()
            .ToDictionary(g => g.GetProperty("key").GetString()!, g => g.GetProperty("count").GetInt32());
        Assert.Equal((await Get(admin, $"/api/identity/users?filter={Q("language eq 'ar'")}")).GetProperty("total").GetInt32(), byLanguage.GetValueOrDefault("ar"));
        Assert.Equal(JsonValueKind.Null, (await Get(admin, "/api/identity/users")).GetProperty("groups").ValueKind);
    }

    [Fact]
    public async Task The_roles_list_serves_the_same_contract_in_memory()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var all = await Get(admin, "/api/identity/roles");
        Assert.True(Items(all)[0].GetProperty("isSystem").GetBoolean(), "system roles first by default");
        var system = await Get(admin, $"/api/identity/roles?filter={Q("isSystem eq true")}");
        Assert.All(Items(system), r => Assert.True(r.GetProperty("isSystem").GetBoolean()));
        var byUsers = Items(await Get(admin, "/api/identity/roles?sort=-userCount")).Select(r => r.GetProperty("userCount").GetInt32()).ToList();
        Assert.Equal(byUsers.OrderDescending(), byUsers);
        var search = await Get(admin, $"/api/identity/roles?search={Q("ADMINISTRATOR")}");
        Assert.Contains(Items(search), r => r.GetProperty("nameEn").GetString() == "Administrator");
        var searchArabic = await Get(admin, $"/api/identity/roles?search={Q("مدير")}");
        Assert.Contains(Items(searchArabic), r => r.GetProperty("nameEn").GetString() == "Administrator");
        var grouped = await Get(admin, "/api/identity/roles?groupBy=isSystem");
        var groups = grouped.GetProperty("groups").EnumerateArray().ToList();
        Assert.Equal(Items(all).Sum(r => r.GetProperty("userCount").GetInt32()),
            groups.Sum(g => decimal.Parse(g.GetProperty("totals").GetProperty("userCount").GetString()!, System.Globalization.CultureInfo.InvariantCulture)));
        var first = await Get(admin, "/api/identity/roles?take=1");
        var second = await Get(admin, $"/api/identity/roles?take=1&after={Q(first.GetProperty("next").GetString()!)}");
        Assert.Equal(Ids(all)[1], Ids(second)[0]);
        await Problem(admin, $"/api/identity/roles?filter={Q("userCount gt 'many'")}");
    }
    /// <summary>Initials (critic p05 round 7): a one-word search finds a user by the first letters of
    /// their name's words, after the users the word itself matches; the database keeps the initials
    /// from the name (a rename moves them) and the audit trail records the name, not the initials.</summary>
    [Fact]
    public async Task A_user_is_found_by_the_initials_of_their_name_and_the_initials_follow_a_rename()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var tag = Guid.NewGuid().ToString("N")[..6];
        // Initials of letters no seeded name or address holds: "qzxw" and, after the rename, "qzxv".
        var created = await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email = $"initials.{tag}@{Env.TenantA.EmailDomain}", displayName = "Qadir Zayed Xavier-Wahid", language = "en", roleIds = Array.Empty<Guid>(),
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.Clone();
        var id = user.GetProperty("id").GetString()!;
        var found = await Get(admin, $"/api/identity/users?search={Q("QZXW")}");
        Assert.Equal([id], Ids(found));
        Assert.True(found.GetProperty("ranked").GetBoolean());
        // Two words are words, never initials.
        Assert.Empty(Ids(await Get(admin, $"/api/identity/users?search={Q("qz xw")}")));

        var renamed = await admin.PutAsJsonAsync($"/api/identity/users/{id}", new
        {
            displayName = "Qadir Zayed Xavier Victor", language = "en", isActive = true, version = user.GetProperty("version").GetUInt32(),
        });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Empty(Ids(await Get(admin, $"/api/identity/users?search=qzxw")));
        Assert.Equal([id], Ids(await Get(admin, $"/api/identity/users?search=qzxv")));

        await using var owner = await Env.OpenAdminAsync();
        await using var audit = new Npgsql.NpgsqlCommand(
            "SELECT count(*) FILTER (WHERE changes ? 'display_name'), count(*) FILTER (WHERE changes ? 'name_initials') FROM audit.entries WHERE table_name = 'users' AND record_id = @id", owner);
        audit.Parameters.AddWithValue("id", Guid.Parse(id));
        await using var reader = await audit.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt64(0));
        Assert.Equal(0, reader.GetInt64(1));
    }
}
