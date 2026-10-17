using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.G2;

namespace Erp.Gates.Tests.Critic;

/// <summary>Critic p00 r10: proves the plants are live (and that the product without them is right).</summary>
public sealed class CriticR10Proof(G2Fixture fixture) : IClassFixture<G2Fixture>
{
    /// <summary>P5: a no-roles user who puts their session token in ?access_token= must still be denied.</summary>
    [Fact]
    public async Task No_roles_user_with_token_in_query_is_denied()
    {
        var env = fixture.Env;
        using var anonymous = env.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/auth/sign-in", new { email = env.Email(env.TenantA, "noaccess"), password = Erp.Testing.ErpTestEnvironment.Password, issueToken = true });
        var signIn = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, signIn);
        var token = JsonDocument.Parse(signIn).RootElement.GetProperty("token").GetString();
        using var client = env.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        using var read = await client.GetAsync($"/api/identity/users?take=1&access_token={token}");
        var body = await read.Content.ReadAsStringAsync();
        Assert.True(read.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"no-roles user, token in ?access_token=: GET /api/identity/users -> {(int)read.StatusCode} {body[..Math.Min(300, body.Length)]}");
    }
}
