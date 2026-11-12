using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.G2;
using Erp.Testing;

namespace Erp.Gates.Tests.Critic;

/// <summary>Critic p00 r17: proves each plant is live, and the product without it is right.</summary>
public sealed class CriticR17Proof(G2Fixture fixture) : IClassFixture<G2Fixture>
{
    private async Task<string> NoRoles(HttpClient admin)
    {
        var email = $"critic.r17.{Guid.NewGuid():N}@{fixture.Env.TenantA.EmailDomain}";
        await G2PermissionTests.CreateUserHoldingAsync(admin, email, [], "Critic r17 " + Guid.NewGuid().ToString("N")[..6], "ناقد " + Guid.NewGuid().ToString("N")[..6]);
        return email;
    }

    /// <summary>P17, P13, P16: a user with no roles is denied whatever form the request takes.</summary>
    [Fact]
    public async Task A_user_with_no_roles_is_denied_whatever_the_path_form_or_host()
    {
        var env = fixture.Env;
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var email = await NoRoles(admin);
        using var user = await env.SignInAsync(email);
        var answers = new List<string>();
        foreach (var (path, host) in new[] { ("/api/identity/users?take=1", (string?)null), ("/api/identity/users/?take=1", null),
                     ("/api/identity/roles/?take=1", null), ("/API/identity/users?take=1", null), ("/api/identity/users?take=1", "app") })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (host is not null) request.Headers.Host = host;
            using var response = await user.SendAsync(request);
            answers.Add($"GET {path} host {host ?? "-"} -> {(int)response.StatusCode}");
        }
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", answers));
        Assert.True(answers.All(a => a.EndsWith("-> 403", StringComparison.Ordinal)), string.Join("\n", answers));
    }

    /// <summary>L17: tenant A's admin never sees tenant B's users, whatever header or path form it sends.</summary>
    [Fact]
    public async Task Tenant_A_never_reads_tenant_B_users_through_a_header_on_a_folder_path()
    {
        var env = fixture.Env;
        using var adminA = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        using var adminB = await env.SignInAsync(env.Email(env.TenantB, "admin"));
        var word = "crl17" + Guid.NewGuid().ToString("N")[..10];
        var made = await adminB.PostAsJsonAsync("/api/identity/users", new { email = $"{word}0@{env.TenantB.EmailDomain}", displayName = $"{word} 0", language = "en", password = ErpTestEnvironment.Password, roleIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        var answers = new List<string>();
        foreach (var path in new[] { $"/api/identity/users?search={word}", $"/api/identity/users/?search={word}" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("X-Tenant-Id", env.TenantB.Id.ToString());
            using var response = await adminA.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            answers.Add($"GET {path} X-Tenant-Id B -> {(int)response.StatusCode}, carries B's user: {text.Contains(word + "0@", StringComparison.Ordinal)}");
        }
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", answers));
        Assert.True(answers.All(a => a.EndsWith("carries B's user: False", StringComparison.Ordinal)), string.Join("\n", answers));
    }

    /// <summary>L18: an anonymous caller never reads anything of a workspace through the health check.</summary>
    [Fact]
    public async Task The_health_check_answers_nothing_of_any_workspace()
    {
        var env = fixture.Env;
        using var anonymous = env.CreateClient();
        using var response = await anonymous.GetAsync($"/api/health?workspace={env.TenantB.Id}");
        var text = await response.Content.ReadAsStringAsync();
        TestContext.Current.TestOutputHelper?.WriteLine($"GET /api/health?workspace=B -> {(int)response.StatusCode} {text}");
        Assert.DoesNotContain("@", text, StringComparison.Ordinal);
        Assert.Equal("{\"status\":\"ok\"}", text);
    }
}
