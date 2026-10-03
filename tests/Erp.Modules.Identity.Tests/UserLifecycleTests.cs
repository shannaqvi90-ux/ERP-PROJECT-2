using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

/// <summary>Inviting with a one-time set-up code, choosing a password, changing it, resetting
/// another user's password, ending sessions, effective access and copying roles.</summary>
public sealed class UserLifecycleTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private ErpTestEnvironment Env => fixture.Env;
    private string AdminA => Env.Email(Env.TenantA, "admin");

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private string NewEmail(string local) => $"{local}.{Guid.NewGuid():N}"[..(local.Length + 9)] + $"@{Env.TenantA.EmailDomain}";

    private async Task<Guid> ReadOnlyRoleAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
        .Single(r => r.GetProperty("nameEn").GetString()!.StartsWith("Read-only", StringComparison.Ordinal)).GetProperty("id").GetGuid();

    [Fact]
    public async Task An_invited_user_signs_in_once_with_the_set_up_code_and_chooses_a_password()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var email = NewEmail("invited");
        var created = await admin.PostAsJsonAsync("/api/identity/users",
            new { email, displayName = "Invited Person", language = "ar", roleIds = new[] { await ReadOnlyRoleAsync(admin) } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = await Json(created);
        var code = user.GetProperty("setupCode").GetString()!;
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", code);
        Assert.True(user.GetProperty("pendingSetup").GetBoolean());
        Assert.True(user.GetProperty("setupCodeExpiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddDays(6));

        // The code alone does not start a session: a new password is required.
        using var client = Env.CreateClient();
        var first = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = code });
        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Equal("auth.passwordChangeRequired", (await Json(first)).GetProperty("code").GetString());
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("authenticated").GetBoolean());

        // A weak new password is refused field by field, in Arabic for an Arabic request.
        using var weak = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-in") { Content = JsonContent.Create(new { email, password = code, newPassword = "short" }) };
        weak.Headers.AcceptLanguage.ParseAdd("ar");
        var refused = await client.SendAsync(weak);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var error = (await Json(refused)).GetProperty("errors").GetProperty("newPassword")[0];
        Assert.Equal("passwordTooShort", error.GetProperty("code").GetString());
        Assert.Matches("[؀-ۿ]", error.GetProperty("message").GetString()!);

        var chosen = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = code, newPassword = "My-Own-Password-77" });
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        Assert.Equal("ar", (await Json(chosen)).GetProperty("user").GetProperty("language").GetString());

        // The code is spent; the chosen password works.
        using var again = Env.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await again.PostAsJsonAsync("/api/auth/sign-in", new { email, password = code })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await again.PostAsJsonAsync("/api/auth/sign-in", new { email, password = "My-Own-Password-77" })).StatusCode);
        var reloaded = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{user.GetProperty("id").GetGuid()}");
        Assert.False(reloaded.GetProperty("pendingSetup").GetBoolean());
        Assert.False(reloaded.TryGetProperty("setupCode", out _), "A set-up code is shown only once");
    }

    [Fact]
    public async Task An_expired_set_up_code_fails_like_a_wrong_password()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var email = NewEmail("expired");
        var user = await Json(await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = "Expired", language = "en", roleIds = Array.Empty<Guid>() }));
        await fixture.ExecAsync("UPDATE identity.user_credentials SET expires_at = now() - interval '1 minute' WHERE id = @id", ("id", user.GetProperty("id").GetGuid()));
        using var client = Env.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = user.GetProperty("setupCode").GetString(), newPassword = "Fresh-Password-12" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("auth.signInFailed", (await Json(response)).GetProperty("code").GetString());
        var history = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{user.GetProperty("id").GetGuid()}/sign-ins");
        Assert.Equal("expired", history.GetProperty("items")[0].GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Signed_in_users_change_their_password_by_proving_the_current_one_and_other_sessions_end()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var email = NewEmail("changer");
        await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = "Changer", language = "en", password = "First-Password-1", roleIds = Array.Empty<Guid>() });
        using var laptop = await Env.SignInAsync(email, "First-Password-1");
        using var phone = Env.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.PostAsJsonAsync("/api/auth/sign-in", new { email, password = "Not-The-Password-1", newPassword = "Second-Password-2" })).StatusCode);
        var same = await phone.PostAsJsonAsync("/api/auth/sign-in", new { email, password = "First-Password-1", newPassword = "First-Password-1" });
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
        Assert.Equal("passwordUnchanged", (await Json(same)).GetProperty("errors").GetProperty("newPassword")[0].GetProperty("code").GetString());

        var changed = await phone.PostAsJsonAsync("/api/auth/sign-in", new { email, password = "First-Password-1", newPassword = "Second-Password-2" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.False((await laptop.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("authenticated").GetBoolean(), "Other sessions end with a new password");
        Assert.True((await phone.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("authenticated").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Env.CreateClient().PostAsJsonAsync("/api/auth/sign-in", new { email, password = "First-Password-1" })).StatusCode);
    }

    [Fact]
    public async Task An_administrator_resets_a_password_and_the_user_is_signed_out_everywhere()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var email = NewEmail("forgot");
        var user = await Json(await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = "Forgetful", language = "en", password = "Old-Password-11", roleIds = Array.Empty<Guid>() }));
        var id = user.GetProperty("id").GetGuid();
        using var session = await Env.SignInAsync(email, "Old-Password-11");

        var reset = await admin.PostAsJsonAsync($"/api/identity/users/{id}/password", new { });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var body = await Json(reset);
        Assert.True(body.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(1, body.GetProperty("sessionsEnded").GetInt32());
        Assert.False((await session.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("authenticated").GetBoolean());

        using var client = Env.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = "Old-Password-11" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/sign-in",
            new { email, password = body.GetProperty("setupCode").GetString(), newPassword = "New-Password-22" })).StatusCode);

        // A temporary password chosen by the administrator must be changed at the next sign-in.
        var temporary = await admin.PostAsJsonAsync($"/api/identity/users/{id}/password", new { password = "Temporary-Pass-33" });
        Assert.False((await Json(temporary)).TryGetProperty("setupCode", out _));
        Assert.Equal(HttpStatusCode.Conflict, (await Env.CreateClient().PostAsJsonAsync("/api/auth/sign-in", new { email, password = "Temporary-Pass-33" })).StatusCode);

        // Signing a user out everywhere.
        var permanent = await admin.PostAsJsonAsync($"/api/identity/users/{id}/password", new { password = "Permanent-Pass-44", mustChangePassword = false });
        Assert.Equal(HttpStatusCode.OK, permanent.StatusCode);
        using var a = await Env.SignInAsync(email, "Permanent-Pass-44");
        using var b = await Env.SignInAsync(email, "Permanent-Pass-44");
        var ended = await Json(await admin.PostAsync($"/api/identity/users/{id}/sessions/revoke", null));
        Assert.Equal(2, ended.GetProperty("sessionsEnded").GetInt32());
        Assert.False((await a.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("authenticated").GetBoolean());
        Assert.False((await b.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task The_access_view_says_what_a_user_can_do_and_which_role_grants_it()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var clerk = await Json(await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Access clerk", nameAr = "كاتب", permissions = new[] { "identity.users.read", "identity.roles.read" } }));
        var auditor = await Json(await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Access auditor", nameAr = "مدقق", permissions = new[] { "identity.users.read", "identity.signIns.read" } }));
        var email = NewEmail("access");
        var user = await Json(await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email, displayName = "Access", language = "en", password = ErpTestEnvironment.Password,
            roleIds = new[] { clerk.GetProperty("id").GetGuid(), auditor.GetProperty("id").GetGuid() },
        }));
        // Read by an administrator whose language is Arabic: labels come in Arabic.
        using var arabicAdmin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin.ar"));
        var access = await arabicAdmin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{user.GetProperty("id").GetGuid()}/access");
        Assert.Equal(2, access.GetProperty("roles").GetArrayLength());
        var permissions = access.GetProperty("permissions").EnumerateArray().ToDictionary(p => p.GetProperty("key").GetString()!);
        Assert.Equal(["identity.roles.read", "identity.signIns.read", "identity.users.read"], permissions.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(2, permissions["identity.users.read"].GetProperty("grantedBy").GetArrayLength());
        Assert.Equal(auditor.GetProperty("id").GetGuid(), permissions["identity.signIns.read"].GetProperty("grantedBy")[0].GetGuid());
        Assert.Matches("[؀-ۿ]", permissions["identity.users.read"].GetProperty("label").GetString()!);

        // Exactly the same set the user's own session holds.
        using var client = await Env.SignInAsync(user.GetProperty("email").GetString()!);
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal(permissions.Keys.Order(StringComparer.Ordinal), session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!));
    }

    [Fact]
    public async Task Copying_a_role_keeps_its_permissions_under_a_new_name()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var source = await Json(await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Copy source", nameAr = "مصدر", permissions = new[] { "identity.users.read", "identity.users.create" } }));
        var copy = await admin.PostAsJsonAsync($"/api/identity/roles/{source.GetProperty("id").GetGuid()}/copy", new { nameEn = "Copy target", nameAr = "نسخة" });
        Assert.Equal(HttpStatusCode.Created, copy.StatusCode);
        var role = await Json(copy);
        Assert.NotEqual(source.GetProperty("id").GetGuid(), role.GetProperty("id").GetGuid());
        Assert.Equal(["identity.users.create", "identity.users.read"], role.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!));
        Assert.False(role.GetProperty("isSystem").GetBoolean());
        var duplicate = await admin.PostAsJsonAsync($"/api/identity/roles/{source.GetProperty("id").GetGuid()}/copy", new { nameEn = "Copy target", nameAr = "نسخة" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // The Administrator role can be copied into an editable role.
        var administrator = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray().Single(r => r.GetProperty("isSystem").GetBoolean());
        var adminCopy = await Json(await admin.PostAsJsonAsync($"/api/identity/roles/{administrator.GetProperty("id").GetGuid()}/copy", new { nameEn = "Deputy administrator", nameAr = "نائب المدير" }));
        Assert.Equal(administrator.GetProperty("permissions").GetArrayLength(), adminCopy.GetProperty("permissions").GetArrayLength());
    }

    [Fact]
    public async Task The_permission_catalogue_carries_what_the_matrix_needs()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var permissions = await admin.GetFromJsonAsync<JsonElement>("/api/identity/permissions");
        var reset = permissions.EnumerateArray().Single(p => p.GetProperty("key").GetString() == "identity.users.resetPassword");
        Assert.Equal("identity", reset.GetProperty("module").GetString());
        Assert.Equal("users", reset.GetProperty("resource").GetString());
        Assert.Equal("resetPassword", reset.GetProperty("action").GetString());
        Assert.Equal("Users", reset.GetProperty("resourceLabel").GetString());
        Assert.All(permissions.EnumerateArray(), p => Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("resourceLabel").GetString())));
    }
}
