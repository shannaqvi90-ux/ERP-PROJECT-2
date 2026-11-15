using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.G2;
using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.Critic;

/// <summary>Critic p00 r19: proves each plant is live (fails with the plant on), and passes on the real product.</summary>
public sealed class CriticR19Proof(G2Fixture fixture) : IClassFixture<G2Fixture>
{
    private static void Log(IEnumerable<string> lines)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", lines));
        try { File.AppendAllText("/out/proof-lines.txt", $"[{Environment.GetEnvironmentVariable("CRITIC_PLANT")}] " + string.Join("\n", lines) + "\n"); } catch { }
    }

    /// <summary>P19: a user holding only identity.users.update cannot read the users list.</summary>
    [Fact]
    public async Task An_update_permission_does_not_open_the_read()
    {
        var env = fixture.Env;
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var email = $"critic.r19p19.{Guid.NewGuid():N}@{env.TenantA.EmailDomain}";
        await G2PermissionTests.CreateUserHoldingAsync(admin, email, ["identity.users.update"], "Critic r19 " + Guid.NewGuid().ToString("N")[..6], "ناقد " + Guid.NewGuid().ToString("N")[..6]);
        using var user = await env.SignInAsync(email);
        var answers = new List<string>();
        foreach (var path in new[] { "/api/identity/users?take=1", "/api/identity/roles?take=1" })
        {
            using var response = await user.GetAsync(path);
            answers.Add($"identity.users.update only: GET {path} -> {(int)response.StatusCode}");
        }
        Log(answers);
        Assert.True(answers.All(a => a.EndsWith("-> 403", StringComparison.Ordinal)), string.Join("\n", answers));
    }

    /// <summary>P17: a user with no roles is denied a path with a trailing slash.</summary>
    [Fact]
    public async Task A_user_with_no_roles_is_denied_a_trailing_slash_path()
    {
        var env = fixture.Env;
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var email = $"critic.r19p17.{Guid.NewGuid():N}@{env.TenantA.EmailDomain}";
        await G2PermissionTests.CreateUserHoldingAsync(admin, email, [], "Critic r19 " + Guid.NewGuid().ToString("N")[..6], "ناقد " + Guid.NewGuid().ToString("N")[..6]);
        using var user = await env.SignInAsync(email);
        var answers = new List<string>();
        foreach (var path in new[] { "/api/identity/users?take=1", "/api/identity/users/?take=1", "/api/identity/roles/?take=1" })
        {
            using var response = await user.GetAsync(path);
            answers.Add($"no roles: GET {path} -> {(int)response.StatusCode}");
        }
        Log(answers);
        Assert.True(answers.All(a => a.EndsWith("-> 403", StringComparison.Ordinal)), string.Join("\n", answers));
    }

    /// <summary>L19: a deep page of tenant A's users list carries A's own total, even right after tenant B asked the same page.</summary>
    [Fact]
    public async Task A_deep_page_total_is_the_callers_own_after_another_tenant_asked()
    {
        var env = fixture.Env;
        using var adminA = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        using var adminB = await env.SignInAsync(env.Email(env.TenantB, "admin"));
        var lines = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            var b = await adminB.PostAsJsonAsync("/api/identity/users", new { email = $"crl19{Guid.NewGuid():N}@{env.TenantB.EmailDomain}", displayName = "crl19 " + i, language = "en", password = ErpTestEnvironment.Password, roleIds = Array.Empty<Guid>() });
            lines.Add($"B creates a user: {(int)b.StatusCode}");
        }
        int Total(string text) => JsonDocument.Parse(text).RootElement.GetProperty("total").GetInt32();
        var first = await adminA.GetStringAsync("/api/identity/users?take=1");
        var deepB = await adminB.GetAsync("/api/identity/users?take=1&skip=5000");
        var textB = await deepB.Content.ReadAsStringAsync();
        var deepA = await adminA.GetAsync("/api/identity/users?take=1&skip=5000");
        var textA = await deepA.Content.ReadAsStringAsync();
        lines.Add($"A first page total {Total(first)}; B deep page {(int)deepB.StatusCode} {textB[..Math.Min(200, textB.Length)]}; A deep page {(int)deepA.StatusCode} {textA[..Math.Min(200, textA.Length)]}");
        Log(lines);
        Assert.Equal(Total(first), Total(textA));
    }
}
