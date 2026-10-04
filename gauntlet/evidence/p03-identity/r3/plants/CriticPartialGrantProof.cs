using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

/// <summary>Critic p03 round 3 proof. A clerk who holds identity permissions only acts on a user
/// or role whose grants lie partly OUTSIDE the identity module (workspace settings). Every answer
/// must be 403; with plants P14-P16 they are 2xx. Copy into tests/Erp.Modules.Identity.Tests/ and
/// run with --filter CriticPartialGrantProof.</summary>
public sealed class CriticPartialGrantProof(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private string NewEmail(string local) => $"{local}.{Guid.NewGuid():N}"[..(local.Length + 9)] + $"@{Env.TenantA.EmailDomain}";

    private static async Task<JsonElement> CreatedAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task A_clerk_cannot_act_on_a_workspace_manager()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var log = TestContext.Current.TestOutputHelper!;
        // The workspace manager: may change the workspace settings, and nothing in identity.
        var managerRole = await CreatedAsync(await admin.PostAsJsonAsync("/api/identity/roles",
            new { nameEn = "Workspace manager", nameAr = "مدير مساحة العمل", permissions = new[] { "tenancy.tenant.read", "tenancy.tenant.update" } }));
        var managerEmail = NewEmail("manager");
        var manager = await CreatedAsync(await admin.PostAsJsonAsync("/api/identity/users",
            new { email = managerEmail, displayName = "Manager", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { managerRole.GetProperty("id").GetGuid() } }));
        var managerId = manager.GetProperty("id").GetGuid();

        // The clerk: identity permissions only, no tenancy permission at all.
        var clerkRole = await CreatedAsync(await admin.PostAsJsonAsync("/api/identity/roles",
            new { nameEn = "Helpdesk clerk", nameAr = "موظف الدعم", permissions = new[] { "identity.users.read", "identity.users.update", "identity.users.resetPassword", "identity.roles.read", "identity.roles.update", "identity.roles.create" } }));
        var clerkEmail = NewEmail("clerk");
        await CreatedAsync(await admin.PostAsJsonAsync("/api/identity/users",
            new { email = clerkEmail, displayName = "Clerk", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { clerkRole.GetProperty("id").GetGuid() } }));
        var helperEmail = NewEmail("helper");
        var helper = await CreatedAsync(await admin.PostAsJsonAsync("/api/identity/users",
            new { email = helperEmail, displayName = "Helper", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = Array.Empty<Guid>() }));
        using var clerk = await Env.SignInAsync(clerkEmail);

        // 1. Reset the workspace manager's password to one the clerk knows (account takeover).
        var reset = await clerk.PostAsJsonAsync($"/api/identity/users/{managerId}/password", new { password = "Clerk-Knows-This-1", mustChangePassword = false });
        log.WriteLine($"reset manager's password: {(int)reset.StatusCode} {await reset.Content.ReadAsStringAsync()}");
        // 2. Change the manager's sign-in address.
        var mgr = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{managerId}");
        var move = await clerk.PutAsJsonAsync($"/api/identity/users/{managerId}", new { displayName = "Manager", language = "en", isActive = true, roleIds = new[] { managerRole.GetProperty("id").GetGuid() }, version = mgr.GetProperty("version").GetUInt32(), email = NewEmail("taken") });
        log.WriteLine($"move manager's sign-in: {(int)move.StatusCode}");
        // 3. Give a colleague the workspace-manager role (a grant the clerk does not hold).
        var h = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{helper.GetProperty("id").GetGuid()}");
        var grant = await clerk.PutAsJsonAsync($"/api/identity/users/{helper.GetProperty("id").GetGuid()}", new { displayName = "Helper", language = "en", isActive = true, roleIds = new[] { managerRole.GetProperty("id").GetGuid() }, version = h.GetProperty("version").GetUInt32() });
        log.WriteLine($"grant helper the manager role: {(int)grant.StatusCode}");
        // 4. Create a role granting tenancy.tenant.update (beyond the clerk's own grants).
        var create = await clerk.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Clerk made", nameAr = "صنع الموظف", permissions = new[] { "tenancy.tenant.update" } });
        log.WriteLine($"create role granting tenancy.tenant.update: {(int)create.StatusCode}");
        // 5. Strip the workspace manager role of what it grants.
        var r = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/roles/{managerRole.GetProperty("id").GetGuid()}");
        var strip = await clerk.PutAsJsonAsync($"/api/identity/roles/{managerRole.GetProperty("id").GetGuid()}", new { nameEn = "Leavers", nameAr = "المغادرون", permissions = Array.Empty<string>(), version = r.GetProperty("version").GetUInt32() });
        log.WriteLine($"rename and empty the manager role: {(int)strip.StatusCode}");

        var signIn = await Env.CreateClient().PostAsJsonAsync("/api/auth/sign-in", new { email = managerEmail, password = "Clerk-Knows-This-1" });
        log.WriteLine($"sign in as the manager with the clerk's password: {(int)signIn.StatusCode}");

        Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, move.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, strip.StatusCode);
    }
}
