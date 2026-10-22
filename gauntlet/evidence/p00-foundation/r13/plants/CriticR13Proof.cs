using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.G2;
using Erp.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.Critic;

/// <summary>Sets every request's client address to loopback, as a reverse proxy or a job on the
/// same machine would arrive (critic p00 r13).</summary>
internal sealed class LoopbackClients : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, call) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            await call(context);
        });
        next(app);
    };
}

/// <summary>Critic p00 r13: proves each plant is live, and the product without it is right.</summary>
public sealed class CriticR13Proof(G2Fixture fixture) : IClassFixture<G2Fixture>
{
    private async Task<string> NoRolesUser(HttpClient admin)
    {
        var email = $"critic.noroles.{Guid.NewGuid():N}@{fixture.Env.TenantA.EmailDomain}";
        var user = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = "Critic no roles", language = "en", password = ErpTestEnvironment.Password, roleIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        return email;
    }

    /// <summary>P11: a user whose roles grant nothing is denied, also when the request comes from loopback.</summary>
    [Fact]
    public async Task A_user_with_no_roles_is_denied_from_loopback_too()
    {
        var env = fixture.Env;
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var email = await NoRolesUser(admin);
        using var plain = await env.SignInAsync(email);
        var plainUsers = await plain.GetAsync("/api/identity/users?take=1");
        await using var loopback = env.Factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddTransient<IStartupFilter, LoopbackClients>()));
        using var client = loopback.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Erp-Request", "1");
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = ErpTestEnvironment.Password });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        var answers = new List<string> { $"plain client GET users -> {(int)plainUsers.StatusCode}" };
        foreach (var (method, path, body) in new (string, string, object?)[]
                 {
                     ("GET", "/api/identity/users?take=1", null),
                     ("GET", "/api/identity/roles?take=1", null),
                     ("POST", "/api/identity/roles", new { nameEn = "Made from loopback", nameAr = "من الجهاز نفسه", permissions = new[] { "identity.users.read" } }),
                 })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request);
            answers.Add($"loopback {method} {path} -> {(int)response.StatusCode}");
        }
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", answers));
        Assert.True(answers.All(a => a.EndsWith("-> 403", StringComparison.Ordinal)), string.Join("\n", answers));
    }

    /// <summary>L10: tenant A's list total for a word only tenant B has is 0.</summary>
    [Fact]
    public async Task A_list_total_never_counts_the_other_tenants_rows()
    {
        var env = fixture.Env;
        using var adminA = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        using var adminB = await env.SignInAsync(env.Email(env.TenantB, "admin"));
        var word = "crl10" + Guid.NewGuid().ToString("N")[..10];
        for (var i = 0; i < 3; i++)
        {
            var made = await adminB.PostAsJsonAsync("/api/identity/users", new { email = $"{word}{i}@{env.TenantB.EmailDomain}", displayName = $"{word} {i}", language = "en", password = ErpTestEnvironment.Password, roleIds = Array.Empty<Guid>() });
            Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        }
        var b = JsonDocument.Parse(await adminB.GetStringAsync($"/api/identity/users?search={word}")).RootElement.GetProperty("total").GetInt32();
        var a = JsonDocument.Parse(await adminA.GetStringAsync($"/api/identity/users?search={word}")).RootElement.GetProperty("total").GetInt32();
        TestContext.Current.TestOutputHelper?.WriteLine($"B total {b}, A total {a}");
        Assert.Equal(3, b);
        Assert.Equal(0, a);
    }
}
