using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.CriticProof;

/// <summary>Critic proof for plant T2: tenant A's administrator reads tenant B's access view.</summary>
public sealed class CriticPlantProof(GateFixture fixture)
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task Tenant_A_reads_tenant_B_access_view_through_the_planted_cache()
    {
        using var b = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        var bSession = await b.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var bAdminId = bSession.GetProperty("user").GetProperty("id").GetGuid();
        var bTenantId = bSession.GetProperty("tenant").GetProperty("id").GetString();
        var warm = await b.GetStringAsync($"/api/identity/users/{bAdminId}/access");

        using var a = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var aSession = await a.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.NotEqual(bTenantId, aSession.GetProperty("tenant").GetProperty("id").GetString());
        var response = await a.GetAsync($"/api/identity/users/{bAdminId}/access");
        var text = await response.Content.ReadAsStringAsync();
        TestContext.Current.TestOutputHelper?.WriteLine($"tenant A admin GET /api/identity/users/{bAdminId}/access (a tenant B user) -> {(int)response.StatusCode} {text}");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(warm, text);
    }
}
