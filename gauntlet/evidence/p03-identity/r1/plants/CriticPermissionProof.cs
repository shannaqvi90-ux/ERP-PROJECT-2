using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.CriticProof;

/// <summary>Critic proof for plants P1 and P2.</summary>
public sealed class CriticPermissionProof(GateFixture fixture)
{
    private ErpTestEnvironment Env => fixture.Env;

    private async Task<HttpClient> UserWithAsync(HttpClient admin, string tag, params string[] permissions)
    {
        var role = await (await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"Critic {tag}", nameAr = $"ناقد {tag}", permissions })).Content.ReadFromJsonAsync<JsonElement>();
        var email = $"critic.{tag}@{Env.TenantA.EmailDomain}";
        var created = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = $"Critic {tag}", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { role.GetProperty("id").GetGuid() } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await Env.SignInAsync(email);
    }

    [Fact]
    public async Task P1_a_user_who_may_only_read_sign_in_history_clears_sign_in_pauses()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var reader = await UserWithAsync(admin, "p1", "identity.signIns.read", "identity.users.read");
        var target = await (await admin.PostAsJsonAsync("/api/identity/users", new { email = $"target.p1@{Env.TenantA.EmailDomain}", displayName = "Target", language = "en", password = ErpTestEnvironment.Password, roleIds = Array.Empty<Guid>() })).Content.ReadFromJsonAsync<JsonElement>();
        var response = await reader.PostAsync($"/api/identity/users/{target.GetProperty("id").GetGuid()}/unblock", null);
        TestContext.Current.TestOutputHelper?.WriteLine($"signIns.read + users.read only: POST unblock -> {(int)response.StatusCode}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task P2_a_role_deleter_deletes_a_role_granting_what_they_do_not_hold()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var deleter = await UserWithAsync(admin, "p2", "identity.roles.delete", "identity.roles.read");
        var strong = await (await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Critic user managers", nameAr = "مديرو المستخدمين", permissions = new[] { "identity.users.create", "identity.users.update", "identity.users.resetPassword" } })).Content.ReadFromJsonAsync<JsonElement>();
        var response = await deleter.DeleteAsync($"/api/identity/roles/{strong.GetProperty("id").GetGuid()}");
        TestContext.Current.TestOutputHelper?.WriteLine($"roles.delete + roles.read only: DELETE a role granting users.create/update/resetPassword -> {(int)response.StatusCode}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
