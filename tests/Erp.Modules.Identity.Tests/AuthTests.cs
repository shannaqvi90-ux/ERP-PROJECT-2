using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;
using Npgsql;

namespace Erp.Modules.Identity.Tests;

public sealed class IdentityFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();

    public async Task ExecAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var admin = await Env.OpenAdminAsync();
        await using var command = new NpgsqlCommand(sql, admin);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>Sign-in, sessions, sign-out, lockout, CSRF defence and workspace choice.</summary>
public sealed class AuthTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private ErpTestEnvironment Env => fixture.Env;
    private string AdminA => Env.Email(Env.TenantA, "admin");

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Signing_in_returns_the_session_and_sets_a_strict_http_only_cookie()
    {
        using var client = Env.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/sign-in", new { email = AdminA, password = ErpTestEnvironment.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("erp_session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        var body = await Json(response);
        Assert.True(body.GetProperty("authenticated").GetBoolean());
        Assert.Equal(Env.TenantA.Code, body.GetProperty("tenant").GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("token", out _), "The token travels only in the cookie unless asked for");
        Assert.Contains("identity.users.read", body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal(AdminA, session.GetProperty("user").GetProperty("email").GetString());
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_answer()
    {
        using var client = Env.CreateClient();
        var wrong = await client.PostAsJsonAsync("/api/auth/sign-in", new { email = AdminA, password = "Not-The-Password-1" });
        var unknown = await client.PostAsJsonAsync("/api/auth/sign-in", new { email = "nobody@nowhere.example", password = "Not-The-Password-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        var a = await Json(wrong);
        var b = await Json(unknown);
        Assert.Equal(a.GetProperty("code").GetString(), b.GetProperty("code").GetString());
        Assert.Equal(a.GetProperty("title").GetString(), b.GetProperty("title").GetString());
        Assert.Equal("auth.signInFailed", a.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Five_failures_pause_sign_in_without_saying_so_and_the_pause_ends()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var email = $"lockout@{Env.TenantA.EmailDomain}";
        var created = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = "Lockout", language = "en", password = ErpTestEnvironment.Password, roleIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var client = Env.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            var failed = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = "Wrong-Password-1" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }
        var locked = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = ErpTestEnvironment.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal("auth.signInFailed", (await Json(locked)).GetProperty("code").GetString());

        await fixture.ExecAsync("UPDATE identity.users SET lockout_until = now() - interval '1 second' WHERE email = @e", ("e", email));
        var after = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = ErpTestEnvironment.Password });
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task Signing_out_revokes_the_session_for_good()
    {
        using var anonymous = Env.CreateClient();
        var signIn = await anonymous.PostAsJsonAsync("/api/auth/sign-in", new { email = AdminA, password = ErpTestEnvironment.Password, issueToken = true });
        var token = (await Json(signIn)).GetProperty("token").GetString()!;
        using var bearer = Env.Factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await bearer.GetAsync("/api/identity/users")).StatusCode);

        var signOut = await bearer.PostAsync("/api/auth/sign-out", null);
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/identity/users")).StatusCode);
        var session = await bearer.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.False(session.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task Expired_sessions_and_malformed_tokens_are_refused()
    {
        using var anonymous = Env.CreateClient();
        var signIn = await anonymous.PostAsJsonAsync("/api/auth/sign-in", new { email = AdminA, password = ErpTestEnvironment.Password, issueToken = true });
        var token = (await Json(signIn)).GetProperty("token").GetString()!;
        await fixture.ExecAsync("UPDATE identity.sessions SET expires_at = now() - interval '1 second' WHERE token_hash = @h",
            ("h", Kernel.Security.SessionTokens.Hash(token)));
        using var bearer = Env.Factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/identity/users")).StatusCode);

        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "' OR 1=1 --");
        Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/identity/users")).StatusCode);
    }

    [Fact]
    public async Task Cookie_requests_that_change_data_need_the_request_header()
    {
        using var client = await Env.SignInAsync(AdminA);
        client.DefaultRequestHeaders.Remove("X-Erp-Request");
        var response = await client.PutAsJsonAsync("/api/identity/me/preferences", new { language = "ar" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("request.missingRequestHeader", (await Json(response)).GetProperty("code").GetString());

        var bearer = await Env.SignInWithTokenAsync(AdminA);
        var withToken = await bearer.PutAsJsonAsync("/api/identity/me/preferences", new { language = "en" });
        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
    }

    [Fact]
    public async Task The_same_email_in_two_workspaces_asks_which_one_only_when_both_passwords_match()
    {
        const string shared = "shared.person@both.example";
        using var adminA = await Env.SignInAsync(AdminA);
        using var adminB = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        foreach (var admin in new[] { adminA, adminB })
        {
            var created = await admin.PostAsJsonAsync("/api/identity/users", new { email = shared, displayName = "Shared", language = "en", password = "Shared-Password-1", roleIds = Array.Empty<Guid>() });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var client = Env.CreateClient();
        var choose = await client.PostAsJsonAsync("/api/auth/sign-in", new { email = shared, password = "Shared-Password-1" });
        Assert.Equal(HttpStatusCode.Conflict, choose.StatusCode);
        var problem = await Json(choose);
        Assert.Equal("auth.chooseWorkspace", problem.GetProperty("code").GetString());
        var codes = problem.GetProperty("workspaces").EnumerateArray().Select(w => w.GetProperty("code").GetString()).ToList();
        Assert.Equal(new[] { Env.TenantA.Code, Env.TenantB.Code }.Order(), codes);

        var chosen = await client.PostAsJsonAsync("/api/auth/sign-in", new { email = shared, password = "Shared-Password-1", workspace = Env.TenantB.Code });
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        Assert.Equal(Env.TenantB.Code, (await Json(chosen)).GetProperty("tenant").GetProperty("code").GetString());

        // A different password in one workspace: the matching one is used without asking.
        var user = (await adminA.GetFromJsonAsync<JsonElement>($"/api/identity/users?search={shared}")).GetProperty("items")[0];
        await fixture.ExecAsync("UPDATE identity.users SET password_hash = @h WHERE id = @id",
            ("h", Kernel.Security.PasswordHasher.Hash("Other-Password-2")), ("id", user.GetProperty("id").GetGuid()));
        var direct = await client.PostAsJsonAsync("/api/auth/sign-in", new { email = shared, password = "Other-Password-2" });
        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
        Assert.Equal(Env.TenantA.Code, (await Json(direct)).GetProperty("tenant").GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_session_probe_always_answers_200_even_with_a_stale_or_malformed_session_cookie()
    {
        using var client = Env.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        foreach (var cookie in new[] { null, new string('A', 43), "not-a-token" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/session");
            if (cookie is not null)
            {
                request.Headers.Add("Cookie", $"erp_session={cookie}");
            }
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False((await Json(response)).GetProperty("authenticated").GetBoolean());
        }
    }

    [Fact]
    public async Task Deactivating_a_user_ends_their_sessions()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var email = $"leaver@{Env.TenantA.EmailDomain}";
        var created = await Json(await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = "Leaver", language = "en", password = ErpTestEnvironment.Password, roleIds = Array.Empty<Guid>() }));
        using var leaver = await Env.SignInAsync(email);
        Assert.True((await leaver.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("authenticated").GetBoolean());

        // Signing in changed the row (last sign-in), so edit the current version.
        var current = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{created.GetProperty("id").GetGuid()}");
        Assert.NotEqual(created.GetProperty("version").GetUInt32(), current.GetProperty("version").GetUInt32());
        var update = await admin.PutAsJsonAsync($"/api/identity/users/{created.GetProperty("id").GetGuid()}", new
        {
            displayName = "Leaver",
            language = "en",
            isActive = false,
            roleIds = Array.Empty<Guid>(),
            version = current.GetProperty("version").GetUInt32(),
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.False((await leaver.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("authenticated").GetBoolean());
        using var again = Env.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await again.PostAsJsonAsync("/api/auth/sign-in", new { email, password = ErpTestEnvironment.Password })).StatusCode);
    }

    [Fact]
    public async Task A_suspended_workspace_cannot_be_signed_in_to()
    {
        var email = Env.Email(Env.TenantB, "viewer");
        await fixture.ExecAsync("UPDATE tenancy.tenants SET status = 'suspended' WHERE id = @id", ("id", Env.TenantB.Id));
        try
        {
            using var client = Env.CreateClient();
            var response = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = ErpTestEnvironment.Password });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await fixture.ExecAsync("UPDATE tenancy.tenants SET status = 'active' WHERE id = @id", ("id", Env.TenantB.Id));
        }
    }
}
