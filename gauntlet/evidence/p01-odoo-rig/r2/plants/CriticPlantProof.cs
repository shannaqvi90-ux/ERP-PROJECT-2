using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>Critic p01 r2 proof: with plant B applied, tenant A's response carries tenant B's
/// workspace id, code and name in a response header.</summary>
public sealed class CriticPlantProof(GateFixture fixture)
{
    [Fact]
    public async Task Plant_B_leaks_tenant_B_in_a_response_header()
    {
        var env = fixture.Env;
        using var b = await env.SignInAsync(env.Email(env.TenantB, "admin"));
        using var a = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        (await b.GetAsync("/api/tenancy/tenant")).EnsureSuccessStatusCode();
        using var response = await a.GetAsync("/api/tenancy/tenant");
        var header = response.Headers.TryGetValues("X-Previous-Workspace", out var v) ? Uri.UnescapeDataString(string.Join(",", v)) : "";
        TestContext.Current.TestOutputHelper?.WriteLine($"tenant A received header X-Previous-Workspace: {header}");
        Assert.Contains(env.TenantB.Id.ToString(), header);
    }
}
