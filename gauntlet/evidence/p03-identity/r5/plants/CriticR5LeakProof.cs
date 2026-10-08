using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

/// <summary>Critic p03 r5, plant L3: tenant A must get the same answer for tenant B's company id in companyRoles as for an id that exists nowhere.</summary>
public sealed class CriticR5LeakProof(CompanyRolesFixture fixture) : IClassFixture<CompanyRolesFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task L3_a_company_role_naming_another_workspaces_company_answers_like_an_unknown_id()
    {
        var b = await Env.SignInAsync(Env.Email(Env.Plan.Tenants[1], "admin"), workspace: Env.Plan.Tenants[1].Code);
        var bCompany = (await b.GetFromJsonAsync<JsonElement>("/api/identity/companies")).EnumerateArray().First().GetProperty("id").GetGuid();
        var a = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var role = (await a.GetFromJsonAsync<JsonElement>("/api/identity/roles?take=200")).GetProperty("items").EnumerateArray().First().GetProperty("id").GetGuid();
        async Task<string> Ask(Guid company)
        {
            var r = await a.PostAsJsonAsync("/api/identity/users", new { email = $"l3.{Guid.NewGuid():N}"[..12] + $"@{Env.TenantA.EmailDomain}", displayName = "L3", language = "en", roleIds = Array.Empty<Guid>(), companyRoles = new[] { new { roleId = role, companyId = company } } });
            return $"{(int)r.StatusCode} " + Regex.Replace(await r.Content.ReadAsStringAsync(), "\"traceId\":\"[^\"]*\"", "");
        }
        var forB = await Ask(bCompany);
        var nowhere = await Ask(Guid.NewGuid());
        TestContext.Current.TestOutputHelper?.WriteLine($"B's company: {forB}\nnowhere: {nowhere}");
        Assert.Equal(nowhere, forB);
    }
}
