using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.G2;
using Erp.Testing;

namespace Erp.Gates.Tests.CriticProof;

/// <summary>Critic proof (p03 r2): a user holding only users.update (+ users.read, roles.read) edits
/// the Administrator. Changing the display name must be 403 (the takeover gate checks this); changing
/// only the sign-in e-mail goes through a different code path under plant P5.</summary>
public sealed class CriticEmailProof(TakeoverFixture fixture) : IClassFixture<TakeoverFixture>
{
    [Fact]
    public async Task A_weaker_user_changes_the_administrators_sign_in_address()
    {
        var env = fixture.Env;
        var output = TestContext.Current.TestOutputHelper!;
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var adminId = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("user").GetProperty("id").GetGuid();
        var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Critic editor", nameAr = "محرر", permissions = new[] { "identity.users.update", "identity.users.read", "identity.roles.read" } });
        var roleId = JsonDocument.Parse(await role.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        var email = $"critic.editor@{env.TenantA.EmailDomain}";
        var created = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = "Critic Editor", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { roleId } });
        output.WriteLine($"weak user created: {(int)created.StatusCode}");
        using var weak = await env.SignInAsync(email);
        var target = await weak.GetFromJsonAsync<JsonElement>($"/api/identity/users/{adminId}");
        output.WriteLine($"Administrator before: {target.GetProperty("email").GetString()}");

        var rename = await weak.PutAsJsonAsync($"/api/identity/users/{adminId}", new { displayName = "Renamed", language = target.GetProperty("language").GetString(), isActive = true, roleIds = target.GetProperty("roleIds"), version = target.GetProperty("version").GetUInt32() });
        output.WriteLine($"weak user renames the Administrator: {(int)rename.StatusCode}");

        var steal = await weak.PutAsJsonAsync($"/api/identity/users/{adminId}", new { displayName = target.GetProperty("displayName").GetString(), language = target.GetProperty("language").GetString(), isActive = true, roleIds = target.GetProperty("roleIds"), version = target.GetProperty("version").GetUInt32(), email = "taken.over@attacker.example" });
        output.WriteLine($"weak user changes only the Administrator's sign-in e-mail: {(int)steal.StatusCode} {await steal.Content.ReadAsStringAsync()}");
        var after = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{adminId}");
        output.WriteLine($"Administrator after: {after.GetProperty("email").GetString()}");
        try
        {
            using var again = await env.SignInAsync(env.Email(env.TenantA, "admin"));
            output.WriteLine("Administrator signs in with their address: ok");
        }
        catch (Exception e)
        {
            output.WriteLine($"Administrator signs in with their address: FAILS ({e.Message.Split('\n')[0]})");
        }
    }
}
