using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.G2;

namespace Erp.Gates.Tests.Critic;

/// <summary>Critic p00 r11: with plant P6 live, which state-changing actions does a no-roles user
/// complete on routes naming their own id? Without the plant every one must be 401/403.</summary>
public sealed class CriticR11Escalation(G2Fixture fixture) : IClassFixture<G2Fixture>
{
    [Fact]
    public async Task No_roles_user_cannot_change_anything_on_routes_naming_their_own_id()
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
        async Task Record(string label, Func<Task<HttpResponseMessage>> send)
        {
            using var r = await send();
            var body = await r.Content.ReadAsStringAsync();
            answers.Add($"{label} -> {(int)r.StatusCode} {body[..Math.Min(240, body.Length)]}");
        }
        using var read = await client.GetAsync($"/api/identity/users/{self}");
        var me = JsonDocument.Parse(await read.Content.ReadAsStringAsync()).RootElement;
        var version = me.TryGetProperty("version", out var v) ? v.ToString() : "0";
        await Record($"PUT /api/identity/users/{{self}} displayName", () => client.PutAsJsonAsync($"/api/identity/users/{self}",
            new { displayName = "Changed without permission", language = "ar", isActive = true, roleIds = Array.Empty<string>(), version, email = me.GetProperty("email").GetString() }));
        await Record("POST /api/identity/users/{self}/unblock", () => client.PostAsync($"/api/identity/users/{self}/unblock", null));
        await Record("GET /api/identity/users/{self}/sign-ins", () => client.GetAsync($"/api/identity/users/{self}/sign-ins"));
        await Record("GET /api/identity/users/{self}/passkeys", () => client.GetAsync($"/api/identity/users/{self}/passkeys"));
        await Record("GET /api/identity/users/{self}/default-company", () => client.GetAsync($"/api/identity/users/{self}/default-company"));
        await Record("POST /api/identity/users/{self}/sessions/revoke", () => client.PostAsync($"/api/identity/users/{self}/sessions/revoke", null));
        Assert.True(answers.All(a => a.Contains("-> 403") || a.Contains("-> 401")), string.Join("\n", answers));
    }
}
