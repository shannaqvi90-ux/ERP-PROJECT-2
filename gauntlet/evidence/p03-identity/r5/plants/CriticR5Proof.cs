using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

/// <summary>Critic p03 r5: each test asserts the SAFE answer. R1 fails on the shipped product (a real fault);
/// P1-P3 pass on the shipped product and fail with plants/P1-P3-permissions.diff applied.</summary>
public sealed class CriticR5Proof(CompanyRolesFixture fixture) : IClassFixture<CompanyRolesFixture>
{
    private ErpTestEnvironment Env => fixture.Env;
    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<Guid> RoleAsync(HttpClient admin, string name, params string[] permissions)
    {
        var r = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = name, nameAr = $"دور {name}", permissions });
        Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await Json(r)).GetProperty("id").GetGuid();
    }

    private async Task<(Guid Id, string Email)> UserAsync(HttpClient admin, string local, Guid[] roleIds, object[]? companyRoles = null)
    {
        var email = $"{local}@{Env.TenantA.EmailDomain}";
        var r = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = local, language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds, companyRoles = companyRoles ?? [] });
        Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return ((await Json(r)).GetProperty("id").GetGuid(), email);
    }

    private async Task<(HttpClient Admin, Guid Company, Guid AdministratorRole, string[] All)> SetUpAsync()
    {
        var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var company = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/companies")).EnumerateArray().First().GetProperty("id").GetGuid();
        var administrator = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles?take=200")).GetProperty("items").EnumerateArray().First(r => r.GetProperty("isSystem").GetBoolean());
        return (admin, company, administrator.GetProperty("id").GetGuid(), administrator.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray());
    }

    [Fact]
    public async Task R1_all_that_match_leaves_alone_a_user_whose_stronger_role_is_held_in_one_company()
    {
        var (admin, company, _, _) = await SetUpAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        var clerkRole = await RoleAsync(admin, $"R1 clerk {tag}", "identity.users.read", "identity.users.update");
        var managerRole = await RoleAsync(admin, $"R1 manager {tag}", "identity.users.read", "tenancy.companies.update", "tenancy.access.update");
        var manager = await UserAsync(admin, $"r1.manager.{tag}", [], [new { roleId = managerRole, companyId = company }]);
        var clerk = await UserAsync(admin, $"r1.clerk.{tag}", [clerkRole]);
        using var client = await Env.SignInAsync(clerk.Email);
        var single = await client.PutAsJsonAsync($"/api/identity/users/{manager.Id}", new { displayName = "x", language = "en", isActive = false, version = (await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{manager.Id}")).GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.Forbidden, single.StatusCode);
        var bulk = await Json(await client.PostAsJsonAsync("/api/identity/users/matching/active", new { active = false, search = manager.Email, filter = "", expectedCount = 1 }));
        Assert.True(bulk.GetProperty("changed").GetInt32() == 0, $"the clerk deactivated the company manager through 'all that match': {bulk}");
    }

    [Fact]
    public async Task P1_a_permission_held_in_one_company_does_not_let_its_holder_grant_it_everywhere()
    {
        var (admin, company, administrator, _) = await SetUpAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        var ops = await RoleAsync(admin, $"P1 ops {tag}", "identity.users.read", "identity.users.create", "identity.roles.read");
        var caller = await UserAsync(admin, $"p1.caller.{tag}", [ops], [new { roleId = administrator, companyId = company }]);
        using var client = await Env.SignInAsync(caller.Email);
        var r = await client.PostAsJsonAsync("/api/identity/users", new { email = $"p1.new.{tag}@{Env.TenantA.EmailDomain}", displayName = "P1 new", language = "en", roleIds = new[] { administrator } });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task P2_all_that_match_leaves_alone_a_user_holding_a_workspace_role_the_caller_lacks()
    {
        var (admin, _, administrator, _) = await SetUpAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        var clerkRole = await RoleAsync(admin, $"P2 clerk {tag}", "identity.users.read", "identity.users.update");
        var strong = await UserAsync(admin, $"p2.strong.{tag}", [administrator]);
        var clerk = await UserAsync(admin, $"p2.clerk.{tag}", [clerkRole]);
        using var client = await Env.SignInAsync(clerk.Email);
        var bulk = await Json(await client.PostAsJsonAsync("/api/identity/users/matching/active", new { active = false, search = strong.Email, filter = "", expectedCount = 1 }));
        Assert.True(bulk.GetProperty("changed").GetInt32() == 0, $"the clerk deactivated an administrator: {bulk}");
    }

    [Fact]
    public async Task P3_a_clerk_cannot_change_an_administrators_starting_company()
    {
        var (admin, _, administrator, _) = await SetUpAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        var clerkRole = await RoleAsync(admin, $"P3 clerk {tag}", "identity.users.read", "identity.users.update");
        var strong = await UserAsync(admin, $"p3.strong.{tag}", [administrator]);
        var clerk = await UserAsync(admin, $"p3.clerk.{tag}", [clerkRole]);
        using var client = await Env.SignInAsync(clerk.Email);
        var current = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{strong.Id}/default-company");
        var r = await client.PutAsJsonAsync($"/api/identity/users/{strong.Id}/default-company", new { companyId = (Guid?)null, version = current.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
