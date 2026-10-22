using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.G2;
using Erp.Testing;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Gates.Tests.Critic;

/// <summary>Critic p00 r14: proves each plant is live, and the product without it is right.</summary>
public sealed class CriticR14Proof(G2Fixture fixture) : IClassFixture<G2Fixture>
{
    private async Task<string> UserHolding(HttpClient admin, string[] permissions)
    {
        var email = $"critic.r14.{Guid.NewGuid():N}@{fixture.Env.TenantA.EmailDomain}";
        await G2PermissionTests.CreateUserHoldingAsync(admin, email, permissions, "Critic r14 " + Guid.NewGuid().ToString("N")[..6], "ناقد " + Guid.NewGuid().ToString("N")[..6]);
        return email;
    }

    /// <summary>P13: a user with no roles is denied also when the request names the compose service as its Host.</summary>
    [Fact]
    public async Task A_user_with_no_roles_is_denied_when_the_host_is_the_service_name()
    {
        var env = fixture.Env;
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var email = await UserHolding(admin, []);
        using var plain = await env.SignInAsync(email);
        var plainUsers = await plain.GetAsync("/api/identity/users?take=1");
        using var client = env.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false, BaseAddress = new Uri("http://app:8080/") });
        client.DefaultRequestHeaders.Add("X-Erp-Request", "1");
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = ErpTestEnvironment.Password });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        var answers = new List<string> { $"plain client GET users -> {(int)plainUsers.StatusCode}" };
        foreach (var path in new[] { "/api/identity/users?take=1", "/api/identity/roles?take=1" })
        {
            using var response = await client.GetAsync(path);
            answers.Add($"Host app:8080 GET {path} -> {(int)response.StatusCode}");
        }
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", answers));
        Assert.True(answers.All(a => a.EndsWith("-> 403", StringComparison.Ordinal)), string.Join("\n", answers));
    }

    /// <summary>P14: a user holding only identity.users.update cannot list users.</summary>
    [Fact]
    public async Task Update_alone_does_not_open_read()
    {
        var env = fixture.Env;
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var email = await UserHolding(admin, ["identity.users.update"]);
        using var user = await env.SignInAsync(email);
        using var response = await user.GetAsync("/api/identity/users?take=1");
        TestContext.Current.TestOutputHelper?.WriteLine($"update-only GET users -> {(int)response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>L11: tenant A's search answer for a word only tenant B has is empty, also right after B asked.</summary>
    [Fact]
    public async Task A_search_answer_never_carries_the_other_tenants_rows()
    {
        var env = fixture.Env;
        using var adminA = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        using var adminB = await env.SignInAsync(env.Email(env.TenantB, "admin"));
        var word = "crl11" + Guid.NewGuid().ToString("N")[..10];
        for (var i = 0; i < 3; i++)
        {
            var made = await adminB.PostAsJsonAsync("/api/identity/users", new { email = $"{word}{i}@{env.TenantB.EmailDomain}", displayName = $"{word} {i}", language = "en", password = ErpTestEnvironment.Password, roleIds = Array.Empty<Guid>() });
            Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        }
        var bText = await adminB.GetStringAsync($"/api/identity/users?search={word}");
        var aText = await adminA.GetStringAsync($"/api/identity/users?search={word}");
        var b = JsonDocument.Parse(bText).RootElement.GetProperty("total").GetInt32();
        var a = JsonDocument.Parse(aText).RootElement.GetProperty("total").GetInt32();
        TestContext.Current.TestOutputHelper?.WriteLine($"B total {b}, A total {a}; A answer carries B's word: {aText.Contains(word + "0@", StringComparison.Ordinal)}");
        Assert.Equal(3, b);
        Assert.Equal(0, a);
        Assert.DoesNotContain(word + "0@", aText, StringComparison.Ordinal);
    }
}
