using System.Net;
using Erp.Gates.Tests.G2;

namespace Erp.Gates.Tests.Critic;

/// <summary>Critic p00 r9: proves plant P3 is live. A user whose roles grant nothing, signed in with a
/// bearer token, must be denied a permissioned read and a permissioned write.</summary>
public sealed class CriticP3Proof(G2Fixture fixture) : IClassFixture<G2Fixture>
{
    [Fact]
    public async Task No_roles_bearer_user_is_denied()
    {
        var env = fixture.Env;
        using var client = await env.SignInWithTokenAsync(env.Email(env.TenantA, "noaccess"));
        using var read = await client.GetAsync("/api/identity/users?take=1");
        using var write = await client.PostAsync("/api/identity/roles", System.Net.Http.Json.JsonContent.Create(new { name = "critic-p3", nameEn = "critic-p3", nameAr = "critic-p3", permissions = new[] { "identity.users.delete" } }));
        var body = await write.Content.ReadAsStringAsync();
        Assert.True(read.StatusCode == HttpStatusCode.Forbidden && write.StatusCode == HttpStatusCode.Forbidden,
            $"no-roles user with a bearer token: GET /api/identity/users -> {(int)read.StatusCode}, POST /api/identity/roles -> {(int)write.StatusCode} {body[..Math.Min(200, body.Length)]}");
    }
}
