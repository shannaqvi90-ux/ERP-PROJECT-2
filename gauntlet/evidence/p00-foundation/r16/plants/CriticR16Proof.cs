using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.G2;
using Erp.Testing;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Gates.Tests.Critic;

/// <summary>Critic p00 r16: proves each plant is live, and the product without it is right.</summary>
public sealed class CriticR16Proof(G2Fixture fixture) : IClassFixture<G2Fixture>
{
    private async Task<string> UserHolding(HttpClient admin, string[] permissions)
    {
        var email = $"critic.r16.{Guid.NewGuid():N}@{fixture.Env.TenantA.EmailDomain}";
        await G2PermissionTests.CreateUserHoldingAsync(admin, email, permissions, "Critic r16 " + Guid.NewGuid().ToString("N")[..6], "ناقد " + Guid.NewGuid().ToString("N")[..6]);
        return email;
    }

    /// <summary>P16: a user with no roles is denied also when the path is written in capitals.</summary>
    [Fact]
    public async Task A_user_with_no_roles_is_denied_whatever_the_case_of_the_path()
    {
        var env = fixture.Env;
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var email = await UserHolding(admin, []);
        using var user = await env.SignInAsync(email);
        var answers = new List<string>();
        foreach (var path in new[] { "/api/identity/users?take=1", "/API/identity/users?take=1", "/Api/Identity/Roles?take=1" })
        {
            using var response = await user.GetAsync(path);
            answers.Add($"GET {path} -> {(int)response.StatusCode}");
        }
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", answers));
        Assert.True(answers.All(a => a.EndsWith("-> 403", StringComparison.Ordinal)), string.Join("\n", answers));
    }

    /// <summary>L16: tenant A's search answer for a word only tenant B has is empty, also right after B asked.</summary>
    [Fact]
    public async Task A_search_answer_never_carries_the_other_tenants_rows()
    {
        var env = fixture.Env;
        using var adminA = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        using var adminB = await env.SignInAsync(env.Email(env.TenantB, "admin"));
        var word = "crl16" + Guid.NewGuid().ToString("N")[..10];
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
