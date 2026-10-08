using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

/// <summary>
/// "All that match" on the users list: activating or deactivating every user the list's search and
/// filter match in one request (critic p05 round 4: with every matching row selected, only Copy
/// worked). The rules of one user's edit hold for each of them, the count the list showed must
/// still be true, every change is audited with the caller as actor, and nothing outside the match
/// or the workspace changes.
/// </summary>
public sealed class MatchingUsersActiveTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private ErpTestEnvironment Env => fixture.Env;
    private string AdminA => Env.Email(Env.TenantA, "admin");

    private const string Route = "/api/identity/users/matching/active";

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await Json(response);
    }

    private static async Task<JsonElement> CreatedAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await Json(response);
    }

    private async Task<Guid> RoleAsync(HttpClient admin, string tag, string[] permissions) =>
        (await CreatedAsync(await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"Role {tag}", nameAr = $"دور {tag}", permissions })))
        .GetProperty("id").GetGuid();

    private async Task<(Guid Id, string Email)> UserAsync(HttpClient admin, string displayName, Guid[] roles, bool signIn = false)
    {
        var email = $"u.{Guid.NewGuid():N}"[..14] + $"@{Env.TenantA.EmailDomain}";
        var user = await CreatedAsync(await admin.PostAsJsonAsync("/api/identity/users",
            new { email, displayName, language = "en", password = signIn ? ErpTestEnvironment.Password : null, mustChangePassword = false, roleIds = roles }));
        return (user.GetProperty("id").GetGuid(), email);
    }

    private async Task<Dictionary<Guid, bool>> StatesAsync(string marker)
    {
        await using var owner = await Env.OpenAdminAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT id, is_active FROM identity.users WHERE display_name LIKE @m", owner);
        command.Parameters.AddWithValue("m", $"%{marker}%");
        var states = new Dictionary<Guid, bool>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            states[reader.GetGuid(0)] = reader.GetBoolean(1);
        }
        return states;
    }

    private async Task<int> TotalAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/identity/users?take=1&{query}")).GetProperty("total").GetInt32();

    [Fact]
    public async Task Deactivates_every_matching_user_except_the_caller_and_stronger_users_and_audits_each_change()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var marker = "Mtch" + Guid.NewGuid().ToString("N")[..8];
        var updater = await RoleAsync(admin, marker + " updater", ["identity.users.read", "identity.users.update"]);
        var stronger = await RoleAsync(admin, marker + " stronger", ["identity.users.read", "identity.roles.read"]);
        var plain = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            plain.Add((await UserAsync(admin, $"{marker} plain {i}", i % 2 == 0 ? [updater] : [])).Id);
        }
        var strong = (await UserAsync(admin, $"{marker} strong", [stronger])).Id;
        var english = (await UserAsync(admin, $"Other {marker[..4]} person", [])).Id; // shares only part of the word: not matched
        var caller = await UserAsync(admin, $"{marker} caller", [updater], signIn: true);
        using var client = await Env.SignInAsync(caller.Email);
        var before = await StatesAsync(marker);
        Assert.Equal(9, before.Count);

        // One user is inactive already: counted as unchanged.
        await fixture.ExecAsync("UPDATE identity.users SET is_active = false WHERE id = @id", ("id", plain[6]));
        var query = $"search={Uri.EscapeDataString(marker)}";
        var total = await TotalAsync(client, query);
        Assert.Equal(9, total);

        var result = await Ok(await client.PostAsJsonAsync(Route, new { active = false, search = marker, filter = "", expectedCount = total }));
        Assert.Equal(9, result.GetProperty("matched").GetInt32());
        Assert.Equal(6, result.GetProperty("changed").GetInt32());
        Assert.Equal(1, result.GetProperty("unchanged").GetInt32());
        Assert.Equal(1, result.GetProperty("refusedSelf").GetInt32());
        Assert.Equal(1, result.GetProperty("refusedBeyondOwn").GetInt32());

        var after = await StatesAsync(marker);
        Assert.All(plain, id => Assert.False(after[id]));
        Assert.True(after[strong], "a user holding a permission the caller lacks stays as they were");
        Assert.True(after[caller.Id], "the caller is never deactivated");
        var others = await StatesAsync("Other " + marker[..4]);
        Assert.True(others[english], "a user outside the match stays as they were");

        // Each change is in the audit trail, with the caller as actor and the old and new state.
        await using var owner = await Env.OpenAdminAsync();
        await using (var command = new Npgsql.NpgsqlCommand("""
            SELECT record_id, actor_id, changes -> 'is_active' ->> 'old', changes -> 'is_active' ->> 'new'
              FROM audit.entries WHERE table_schema = 'identity' AND table_name = 'users' AND action = 'update' AND record_id = ANY(@ids) AND changes ? 'is_active'
            """, owner))
        {
            command.Parameters.AddWithValue("ids", plain.Take(6).ToArray());
            var audited = new List<Guid>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                audited.Add(reader.GetGuid(0));
                Assert.Equal(caller.Id, reader.GetGuid(1));
                Assert.Equal("true", reader.GetString(2));
                Assert.Equal("false", reader.GetString(3));
            }
            Assert.Equal(plain.Take(6).OrderBy(i => i), audited.OrderBy(i => i));
        }

        // The caller's own session still works: they were never deactivated.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/identity/users?take=1")).StatusCode);

        // Activating the same match brings the seven back; the caller and the stronger user are
        // active already, so nothing is refused.
        var back = await Ok(await client.PostAsJsonAsync(Route, new { active = true, search = marker, expectedCount = 9 }));
        Assert.Equal(7, back.GetProperty("changed").GetInt32());
        Assert.Equal(2, back.GetProperty("unchanged").GetInt32());
        Assert.Equal(0, back.GetProperty("refusedSelf").GetInt32());
        Assert.Equal(0, back.GetProperty("refusedBeyondOwn").GetInt32());
        Assert.All(await StatesAsync(marker), p => Assert.True(p.Value));
    }

    [Fact]
    public async Task The_filter_narrows_the_match_and_a_changed_count_changes_nothing()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var marker = "Fltr" + Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 4; i++)
        {
            await UserAsync(admin, $"{marker} {i}", []);
        }
        await fixture.ExecAsync("UPDATE identity.users SET language = 'ar' WHERE display_name IN (@a, @b)", ("a", $"{marker} 0"), ("b", $"{marker} 1"));
        var filter = "language eq 'ar'";
        Assert.Equal(2, await TotalAsync(admin, $"search={marker}&filter={Uri.EscapeDataString(filter)}"));

        // The list said 3 but 2 match now: 409 with both numbers, nothing changed.
        var stale = await admin.PostAsJsonAsync(Route, new { active = false, search = marker, filter, expectedCount = 3 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await Json(stale);
        Assert.Equal("list.matchingChanged", problem.GetProperty("code").GetString());
        Assert.Contains("3", problem.GetProperty("title").GetString());
        Assert.Contains("2", problem.GetProperty("title").GetString());
        Assert.All(await StatesAsync(marker), p => Assert.True(p.Value));

        var done = await Ok(await admin.PostAsJsonAsync(Route, new { active = false, search = marker, filter, expectedCount = 2 }));
        Assert.Equal(2, done.GetProperty("changed").GetInt32());
        var states = await StatesAsync(marker);
        Assert.Equal(2, states.Count(p => !p.Value));
        Assert.Equal(2, states.Count(p => p.Value));
    }

    [Fact]
    public async Task Bad_requests_are_refused_by_field_and_change_nothing()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var marker = "Bad" + Guid.NewGuid().ToString("N")[..8];
        await UserAsync(admin, $"{marker} one", []);

        var missing = await admin.PostAsJsonAsync(Route, new { search = marker });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var errors = (await Json(missing)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("active", out _));
        Assert.True(errors.TryGetProperty("expectedCount", out _));

        var negative = await admin.PostAsJsonAsync(Route, new { active = false, search = marker, expectedCount = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.True((await Json(negative)).GetProperty("errors").TryGetProperty("expectedCount", out _));

        // A filter the list itself would refuse is refused the same way, naming the parameter.
        var refused = await admin.PostAsJsonAsync(Route, new { active = false, filter = "nosuchcolumn eq 1", expectedCount = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var error = (await Json(refused)).GetProperty("errors").GetProperty("filter")[0];
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("code").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));

        Assert.All(await StatesAsync(marker), p => Assert.True(p.Value));
    }

    [Fact]
    public async Task Without_the_update_permission_the_request_is_forbidden_and_changes_nothing()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var marker = "Perm" + Guid.NewGuid().ToString("N")[..8];
        var reader = await RoleAsync(admin, marker + " reader", ["identity.users.read"]);
        await UserAsync(admin, $"{marker} target", []);
        var caller = await UserAsync(admin, $"{marker} reader", [reader], signIn: true);
        using var client = await Env.SignInAsync(caller.Email);
        var response = await client.PostAsJsonAsync(Route, new { active = false, search = marker, expectedCount = 2 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.All(await StatesAsync(marker), p => Assert.True(p.Value));
    }

    [Fact]
    public async Task Another_workspaces_users_are_never_matched_or_changed()
    {
        using var adminA = await Env.SignInAsync(AdminA);
        using var adminB = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        var marker = "Tnt" + Guid.NewGuid().ToString("N")[..8];
        var inB = await CreatedAsync(await adminB.PostAsJsonAsync("/api/identity/users",
            new { email = $"b.{Guid.NewGuid():N}"[..14] + $"@{Env.TenantB.EmailDomain}", displayName = $"{marker} in B", language = "en", roleIds = Array.Empty<Guid>() }));
        await UserAsync(adminA, $"{marker} in A", []);

        // Tenant A matches only its own user, whatever count it claims, and B's user is untouched.
        var wrongCount = await adminA.PostAsJsonAsync(Route, new { active = false, search = marker, expectedCount = 2 });
        Assert.Equal(HttpStatusCode.Conflict, wrongCount.StatusCode);
        var result = await Ok(await adminA.PostAsJsonAsync(Route, new { active = false, search = marker, expectedCount = 1 }));
        Assert.Equal(1, result.GetProperty("changed").GetInt32());
        var states = await StatesAsync(marker);
        Assert.True(states[inB.GetProperty("id").GetGuid()], "tenant B's user stays active");
        Assert.Equal(1, states.Count(p => !p.Value));
    }
}
