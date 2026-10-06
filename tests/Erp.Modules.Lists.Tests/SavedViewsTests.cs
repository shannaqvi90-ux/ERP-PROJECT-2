using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Lists.Tests;

public sealed class ListsFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// Saved views of a list: personal views stay private to their owner (even inside the workspace),
/// shared views reach every reader of the list and only sharers change them, one default per
/// owner, names unique per owner, every field validated against the list's own columns and query
/// language with messages in the user's language, optimistic concurrency, and an audit row for
/// every change. The list definition tells screens what each column allows.
/// </summary>
public sealed class SavedViewsTests(ListsFixture fixture) : IClassFixture<ListsFixture>
{
    private const string Users = "/api/lists/identity.users";

    private ErpTestEnvironment Env => fixture.Env;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static object View(string name, bool isDefault = false, string? filter = null, string? sort = null, string? groupBy = null, string? search = null, string[]? columns = null, uint? version = null) =>
        new { name, columns = columns ?? ["displayName", "email"], filter, sort, groupBy, search, isDefault, version };

    [Fact]
    public async Task The_definition_describes_columns_operators_choices_presets_and_sharing()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var definition = await admin.GetFromJsonAsync<JsonElement>($"{Users}/definition");
        Assert.Equal("/api/identity/users", definition.GetProperty("endpoint").GetString());
        Assert.True(definition.GetProperty("canShare").GetBoolean());
        var columns = definition.GetProperty("columns").EnumerateArray().ToDictionary(c => c.GetProperty("key").GetString()!);
        Assert.Equal("choice", columns["language"].GetProperty("type").GetString());
        Assert.Equal(["en", "ar"], columns["language"].GetProperty("choices").EnumerateArray().Select(c => c.GetProperty("value").GetString()));
        Assert.Contains("contains", columns["displayName"].GetProperty("operators").EnumerateArray().Select(o => o.GetString()));
        Assert.DoesNotContain("contains", columns["lastSignInAt"].GetProperty("operators").EnumerateArray().Select(o => o.GetString()));
        Assert.True(columns["createdAt"].GetProperty("hidden").GetBoolean());
        Assert.Equal(["displayName", "email"], definition.GetProperty("searchFields").EnumerateArray().Select(f => f.GetString()));
        // A word written in Arabic letters searches the name and the Arabic name (not the address).
        Assert.Equal(["displayName", "displayNameAr"], definition.GetProperty("arabicSearchFields").EnumerateArray().Select(f => f.GetString()));
        Assert.Contains(definition.GetProperty("presets").EnumerateArray(), p => p.GetProperty("key").GetString() == "active" && p.GetProperty("filter").GetString() == "isActive eq true");

        using var viewer = await Env.SignInAsync(Env.Email(Env.TenantA, "viewer"));
        Assert.False((await viewer.GetFromJsonAsync<JsonElement>($"{Users}/definition")).GetProperty("canShare").GetBoolean());
    }

    [Fact]
    public async Task Personal_views_are_private_to_their_owner()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var other = await Env.SignInAsync(Env.Email(Env.TenantA, "admin.ar"));
        var created = await admin.PostAsJsonAsync($"{Users}/views", View("Arabic speakers", filter: "language eq 'ar'", sort: "displayName"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var view = await Json(created);
        var id = view.GetProperty("id").GetString();
        Assert.True(view.GetProperty("isMine").GetBoolean());
        Assert.False(view.GetProperty("isShared").GetBoolean());

        var mine = await admin.GetFromJsonAsync<JsonElement>($"{Users}/views");
        Assert.Contains(mine.GetProperty("items").EnumerateArray(), v => v.GetProperty("id").GetString() == id);
        var theirs = await other.GetFromJsonAsync<JsonElement>($"{Users}/views");
        Assert.DoesNotContain(theirs.GetProperty("items").EnumerateArray(), v => v.GetProperty("id").GetString() == id);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Users}/views/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"{Users}/views/{id}", View("Taken over", version: view.GetProperty("version").GetUInt32()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{Users}/views/{id}")).StatusCode);
        // A personal view is not reachable as a shared one, nor through another list.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Users}/shared-views/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/lists/identity.roles/views/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{Users}/views/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Users}/views/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Users}/views/{id}")).StatusCode);
    }

    [Fact]
    public async Task Shared_views_reach_every_reader_and_only_sharers_change_them()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var viewer = await Env.SignInAsync(Env.Email(Env.TenantA, "viewer"));
        var created = await admin.PostAsJsonAsync($"{Users}/shared-views", View("Inactive, by language", filter: "isActive eq false", groupBy: "language"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var view = await Json(created);
        var id = view.GetProperty("id").GetString();
        Assert.True(view.GetProperty("isShared").GetBoolean());
        Assert.False(view.GetProperty("isMine").GetBoolean());

        var seen = await viewer.GetFromJsonAsync<JsonElement>($"{Users}/views");
        var shared = seen.GetProperty("items").EnumerateArray().Single(v => v.GetProperty("id").GetString() == id);
        Assert.Equal("language", shared.GetProperty("groupBy").GetString());
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Users}/shared-views/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"{Users}/shared-views/{id}", View("Mine now", version: view.GetProperty("version").GetUInt32()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"{Users}/shared-views/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"{Users}/shared-views", View("Viewer's"))).StatusCode);
        // A shared view is not changed through the personal route either.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"{Users}/views/{id}", View("x", version: view.GetProperty("version").GetUInt32()))).StatusCode);

        var renamed = await admin.PutAsJsonAsync($"{Users}/shared-views/{id}", View("Inactive users by language", filter: "isActive eq false", groupBy: "language", version: view.GetProperty("version").GetUInt32()));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var stale = await admin.PutAsJsonAsync($"{Users}/shared-views/{id}", View("Stale", version: view.GetProperty("version").GetUInt32()));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrency", (await Json(stale)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Users}/shared-views/{id}")).StatusCode);
    }

    [Fact]
    public async Task One_default_per_owner_and_names_unique_per_owner()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var other = await Env.SignInAsync(Env.Email(Env.TenantA, "admin.ar"));
        var first = await Json(await admin.PostAsJsonAsync($"{Users}/views", View("Default one", isDefault: true)));
        var second = await Json(await admin.PostAsJsonAsync($"{Users}/views", View("Default two", isDefault: true)));
        var otherDefault = await Json(await other.PostAsJsonAsync($"{Users}/views", View("Default one", isDefault: true)));
        Assert.True(otherDefault.GetProperty("isDefault").GetBoolean(), "the same name and a default of another user are independent");

        var views = (await admin.GetFromJsonAsync<JsonElement>($"{Users}/views")).GetProperty("items").EnumerateArray().Where(v => v.GetProperty("isMine").GetBoolean()).ToList();
        Assert.Equal([second.GetProperty("id").GetString()], views.Where(v => v.GetProperty("isDefault").GetBoolean()).Select(v => v.GetProperty("id").GetString()));

        // Making the first the default again moves the default back.
        var current = await admin.GetFromJsonAsync<JsonElement>($"{Users}/views/{first.GetProperty("id").GetString()}");
        var back = await admin.PutAsJsonAsync($"{Users}/views/{first.GetProperty("id").GetString()}", View("Default one", isDefault: true, version: current.GetProperty("version").GetUInt32()));
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        views = (await admin.GetFromJsonAsync<JsonElement>($"{Users}/views")).GetProperty("items").EnumerateArray().Where(v => v.GetProperty("isMine").GetBoolean()).ToList();
        Assert.Equal([first.GetProperty("id").GetString()], views.Where(v => v.GetProperty("isDefault").GetBoolean()).Select(v => v.GetProperty("id").GetString()));

        var duplicate = await admin.PostAsJsonAsync($"{Users}/views", View(" Default two "));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("lists.viewNameTaken", (await Json(duplicate)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Every_field_is_validated_against_the_list_in_the_users_language()
    {
        using var arabic = await Env.SignInAsync(Env.Email(Env.TenantA, "admin.ar"));
        var response = await arabic.PostAsJsonAsync($"{Users}/views", new
        {
            name = "",
            columns = new[] { "displayName", "passwordHash" },
            sort = "language",
            filter = "isActive eq 'yes'",
            search = "a b c d e f g h i",
            groupBy = "email",
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await Json(response)).GetProperty("errors");
        Assert.Equal("required", errors.GetProperty("name")[0].GetProperty("code").GetString());
        Assert.Equal("unknownIds", errors.GetProperty("columns")[0].GetProperty("code").GetString());
        Assert.Equal("list.notSortable", errors.GetProperty("sort")[0].GetProperty("code").GetString());
        Assert.Equal("list.valueBoolean", errors.GetProperty("filter")[0].GetProperty("code").GetString());
        Assert.Equal("list.searchTooManyWords", errors.GetProperty("search")[0].GetProperty("code").GetString());
        Assert.Equal("list.notGroupable", errors.GetProperty("groupBy")[0].GetProperty("code").GetString());
        foreach (var field in errors.EnumerateObject())
        {
            Assert.Matches(@"\p{IsArabic}", field.Value[0].GetProperty("message").GetString()!);
        }

        var noColumns = await arabic.PostAsJsonAsync($"{Users}/views", new { name = "x", columns = Array.Empty<string>() });
        Assert.Equal("required", (await Json(noColumns)).GetProperty("errors").GetProperty("columns")[0].GetProperty("code").GetString());
        var tooLong = await arabic.PostAsJsonAsync($"{Users}/views", View(new string('x', 101)));
        Assert.Equal("maxLength", (await Json(tooLong)).GetProperty("errors").GetProperty("name")[0].GetProperty("code").GetString());
        var noVersion = await arabic.PutAsJsonAsync($"{Users}/views/{Guid.NewGuid()}", View("x"));
        Assert.Equal("required", (await Json(noVersion)).GetProperty("errors").GetProperty("version")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Every_change_to_a_view_is_audited_with_its_author()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var view = await Json(await admin.PostAsJsonAsync($"{Users}/views", View("Audited view", filter: "language eq 'en'")));
        var id = view.GetProperty("id").GetGuid();
        await admin.PutAsJsonAsync($"{Users}/views/{id}", View("Audited view, renamed", version: view.GetProperty("version").GetUInt32()));
        await admin.DeleteAsync($"{Users}/views/{id}");

        await using var db = await Env.OpenAdminAsync();
        await using var command = new Npgsql.NpgsqlCommand("""
            SELECT e.action, e.actor_kind, e.changes ? 'name', u.email
              FROM audit.entries e LEFT JOIN identity.users u ON u.id = e.actor_id
             WHERE e.table_schema = 'lists' AND e.table_name = 'saved_views' AND e.record_id = @id
             ORDER BY e.id
            """, db);
        command.Parameters.AddWithValue("id", id);
        var rows = new List<(string Action, string Kind, bool Name, string? Email)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }
        Assert.Equal(["insert", "update", "delete"], rows.Select(r => r.Action));
        Assert.All(rows, r => Assert.Equal(Env.Email(Env.TenantA, "admin"), r.Email));
        Assert.All(rows, r => Assert.True(r.Name));
    }
}
