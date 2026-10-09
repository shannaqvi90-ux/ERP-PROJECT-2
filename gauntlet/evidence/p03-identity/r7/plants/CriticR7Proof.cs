using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

/// <summary>Critic p03 r7: each test asserts the SAFE answer. All pass on the shipped product; Pf fails with plant Pf
/// (plants/apply-r7-plants.py: PUT /roles/{id} lets permissions held in ONE company count as held everywhere).</summary>
public sealed class CriticR7Proof(CompanyRolesFixture fixture) : IClassFixture<CompanyRolesFixture>
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
    public async Task Pf_a_role_manager_of_one_company_cannot_change_a_role_held_across_the_workspace()
    {
        var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var companies = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/companies")).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.True(companies.Count >= 2);
        var tag = Guid.NewGuid().ToString("N")[..6];
        // The manager holds roles.update and the companies grants, but only through a role in company 1.
        var managerRole = await RoleAsync(admin, $"Pf role manager {tag}", "identity.roles.read", "identity.roles.update", "identity.users.read",
            "tenancy.companies.read", "tenancy.companies.update");
        var manager = await UserAsync(admin, $"pf.manager.{tag}", [], [new { roleId = managerRole, companyId = companies[0] }]);
        await AccessAsync(admin, manager.Id, companies[0], companies[1]);
        // A workspace-wide role granting what the manager holds in company 1 only, held everywhere by someone else.
        var editorRole = await RoleAsync(admin, $"Pf companies editor {tag}", "tenancy.companies.read", "tenancy.companies.update");
        await UserAsync(admin, $"pf.holder.{tag}", [editorRole], []);
        var before = await Json(await admin.GetAsync($"/api/identity/roles/{editorRole}"));
        using var client = await Env.SignInAsync(manager.Email);
        var put = await client.PutAsJsonAsync($"/api/identity/roles/{editorRole}", new
        {
            nameEn = $"Pf renamed {tag}", nameAr = $"معاد {tag}", permissions = new[] { "tenancy.companies.read" }, version = before.GetProperty("version").GetUInt32(),
        });
        var text = await put.Content.ReadAsStringAsync();
        TestContext.Current.TestOutputHelper?.WriteLine($"{(int)put.StatusCode} {text}");
        Assert.True(put.StatusCode == HttpStatusCode.Forbidden,
            $"a user holding the role's grants only in company 1 changed a role held across the workspace: {(int)put.StatusCode} {text}");
        var after = await Json(await admin.GetAsync($"/api/identity/roles/{editorRole}"));
        Assert.Equal(before.GetProperty("nameEn").GetString(), after.GetProperty("nameEn").GetString());
    }
}
