using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

/// <summary>Critic p03 r6: each test asserts the SAFE answer. All pass on the shipped product; Pc fails with plant Pc,
/// L4 with plant L4 (plants/apply-r6-plants.py).</summary>
public sealed class CriticR6Proof(CompanyRolesFixture fixture) : IClassFixture<CompanyRolesFixture>
{
    private ErpTestEnvironment Env => fixture.Env;
    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<Guid> RoleAsync(HttpClient admin, string name, params string[] permissions)
    {
        var r = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = name, nameAr = $"دور {name}", permissions });
        Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await Json(r)).GetProperty("id").GetGuid();
    }

    private async Task<(Guid Id, string Email)> UserAsync(HttpClient admin, string local, Guid[] roleIds, object[] companyRoles)
    {
        var email = $"{local}@{Env.TenantA.EmailDomain}";
        var r = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = local, language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds, companyRoles });
        Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return ((await Json(r)).GetProperty("id").GetGuid(), email);
    }

    private static async Task AccessAsync(HttpClient admin, Guid userId, params Guid[] companies)
    {
        var body = new JsonObject { ["companies"] = new JsonArray(companies.Select(c => (JsonNode)new JsonObject { ["companyId"] = c, ["allBranches"] = true, ["branchIds"] = new JsonArray() }).ToArray()) };
        var current = await admin.GetAsync($"/api/tenancy/access/{userId}");
        if (current.IsSuccessStatusCode && await current.Content.ReadFromJsonAsync<JsonObject>() is { } read && read["version"] is { } v)
        {
            body["version"] = v.DeepClone();
        }
        var r = await admin.PutAsync($"/api/tenancy/access/{userId}", JsonContent.Create(body));
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Pc_all_that_match_does_not_let_a_grant_held_in_one_company_cover_the_same_role_in_another()
    {
        var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var companies = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/companies")).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.True(companies.Count >= 2);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var clerkRole = await RoleAsync(admin, $"Pc clerk {tag}", "identity.users.read", "identity.users.update", "tenancy.companies.read", "tenancy.companies.update");
        var editorRole = await RoleAsync(admin, $"Pc editor {tag}", "identity.users.read", "tenancy.companies.read", "tenancy.companies.update");
        var clerk = await UserAsync(admin, $"pc.clerk.{tag}", [], [new { roleId = clerkRole, companyId = companies[0] }]);
        await AccessAsync(admin, clerk.Id, companies[0], companies[1]);
        var target = await UserAsync(admin, $"pc.target.{tag}", [], [new { roleId = editorRole, companyId = companies[1] }]);
        await AccessAsync(admin, target.Id, companies[0], companies[1]);
        using var client = await Env.SignInAsync(clerk.Email);
        var bulk = await Json(await client.PostAsJsonAsync("/api/identity/users/matching/active", new { active = false, search = target.Email, filter = "", expectedCount = 1 }));
        TestContext.Current.TestOutputHelper?.WriteLine(bulk.ToString());
        Assert.True(bulk.GetProperty("changed").GetInt32() == 0, $"a clerk holding the grants only in company 1 deactivated a user holding them in company 2: {bulk}");
        var control = await UserAsync(admin, $"pc.control.{tag}", [], [new { roleId = editorRole, companyId = companies[0] }]);
        await AccessAsync(admin, control.Id, companies[0]);
        var allowed = await Json(await client.PostAsJsonAsync("/api/identity/users/matching/active", new { active = false, search = control.Email, filter = "", expectedCount = 1 }));
        Assert.True(allowed.GetProperty("changed").GetInt32() == 1, $"control: the same role in company 1 should be within the clerk: {allowed}");
    }

    [Fact]
    public async Task L4_creating_a_user_with_another_workspaces_email_answers_like_an_unused_one()
    {
        var b = await Env.SignInAsync(Env.Email(Env.Plan.Tenants[1], "admin"), workspace: Env.Plan.Tenants[1].Code);
        var bEmail = (await b.GetFromJsonAsync<JsonElement>("/api/identity/users?take=5")).GetProperty("items").EnumerateArray().First().GetProperty("email").GetString()!;
        var a = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        async Task<int> Ask(string email) =>
            (int)(await a.PostAsJsonAsync("/api/identity/users", new { email, displayName = "L4", language = "en", roleIds = Array.Empty<Guid>() })).StatusCode;
        var forB = await Ask(bEmail);
        var unused = await Ask($"l4.{Guid.NewGuid():N}"[..14] + "@" + bEmail.Split('@')[1]);
        TestContext.Current.TestOutputHelper?.WriteLine($"B's e-mail {bEmail}: {forB}; unused: {unused}");
        Assert.Equal(unused, forB);
    }
}
