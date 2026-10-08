using Erp.Testing;

namespace Erp.Gates.Tests.CriticProof;

/// <summary>Critic's proof that planted fault A-hdr is a real leak. Put in tests/Erp.Gates.Tests/CriticProof/ with plant-A-hdr.diff applied.</summary>
public sealed class CriticProofTests(Erp.Gates.Tests.G2.G2Fixture fixture) : IClassFixture<Erp.Gates.Tests.G2.G2Fixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task Planted_header_switches_tenant()
    {
        using var client = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/users?take=5");
        request.Headers.Add("X-Erp-Workspace", Env.TenantB.Id.ToString());
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        TestContext.Current.TestOutputHelper?.WriteLine($"{(int)response.StatusCode} {text[..Math.Min(600, text.Length)]}");
        Assert.Contains("@" + Env.TenantB.EmailDomain, text);
    }
}
