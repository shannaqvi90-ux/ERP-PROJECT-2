using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.G2;

namespace Erp.Gates.Tests.Critic;

/// <summary>Critic p00 r11: proves plant P6 is live (and that the product without it is right).</summary>
public sealed class CriticR11Proof(G2Fixture fixture) : IClassFixture<G2Fixture>
{
    /// <summary>P6: a no-roles user who names their own user id in the route must still be denied.</summary>
    [Fact]
    public async Task No_roles_user_is_denied_on_routes_naming_their_own_id()
    {
        var env = fixture.Env;
        using var anonymous = env.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/auth/sign-in", new { email = env.Email(env.TenantA, "noaccess"), password = Erp.Testing.ErpTestEnvironment.Password, issueToken = true });
        var signIn = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, signIn);
        var root = JsonDocument.Parse(signIn).RootElement;
        var token = root.GetProperty("token").GetString();
        var self = root.GetProperty("user").GetProperty("id").GetString();
        using var client = env.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var answers = new List<string>();
        foreach (var path in new[] { $"/api/identity/users/{self}", $"/api/identity/users/{self}/access", $"/api/tenancy/access/{self}" })
        {
            using var read = await client.GetAsync(path);
            var body = await read.Content.ReadAsStringAsync();
            answers.Add($"GET {path} -> {(int)read.StatusCode} {body[..Math.Min(200, body.Length)]}");
        }
        Assert.True(answers.All(a => a.Contains("-> 403") || a.Contains("-> 401")), string.Join("\n", answers));
    }
}
