using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

public sealed class RateLimitFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() =>
        Env = await ErpTestEnvironment.StartGateAsync(new Dictionary<string, string?>
        {
            ["Erp:RateLimits:SignInPerMinute"] = "3",
            ["Erp:RateLimits:SharedAddressClients"] = "2",
            // Requests name their client address through a known proxy; part of it is declared
            // shared by many clients, as the compose gateway is in the shipped deployment.
            ["Erp:Http:KnownProxies"] = Proxy,
            ["Erp:Auth:SharedAddresses"] = SharedNetwork,
        });

    public const string Proxy = "192.0.2.1";

    public const string SharedNetwork = "203.0.113.240/28";

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>Sign-in attempts per client address are limited (online password guessing).</summary>
public sealed class RateLimitTests(RateLimitFixture fixture) : IClassFixture<RateLimitFixture>
{
    [Fact]
    public async Task Sign_in_attempts_beyond_the_limit_get_429_with_a_localized_problem()
    {
        using var client = fixture.Env.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ar");
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/sign-in", new { email = $"nobody{i}@example.com", password = "Wrong-Password-1" });
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("request.tooMany", problem.GetProperty("code").GetString());
                Assert.Matches(@"\p{IsArabic}", problem.GetProperty("title").GetString()!);
            }
        }
        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task Behind_a_shared_address_each_account_has_a_clients_budget_and_everyone_together_the_shared_ceiling()
    {
        // Critic p03 round 12: behind the shipped compose gateway 30 anonymous requests a minute
        // stopped every sign-in of every workspace. Behind an address many clients share, each
        // account typed is a client of its own (its e-mail address normalised as sign-in does, so
        // case and spaces give no fresh budget); everyone behind the address together stays within
        // SharedAddressClients clients' budget; an address of one client keeps one budget across
        // every account, as before.
        var tag = Guid.NewGuid().ToString("N")[..8];
        const string gateway = "203.0.113.244";
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add(await AttemptAsync(gateway, $"target.{tag}@example.com"));
        }
        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        // The same account, written differently: its budget is spent.
        await AssertTooManyAsync(gateway, $"  Target.{tag}@EXAMPLE.com ");
        // Another person behind the same address still gets an answer of their own.
        Assert.Equal(HttpStatusCode.Unauthorized, await AttemptAsync(gateway, $"colleague.{tag}@example.com"));

        // Everyone behind the address together: at most 2 clients' budget (6 a minute), then the
        // address's ceiling holds for every account.
        var answers = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            answers.Add(await AttemptAsync(gateway, $"spray{i}.{tag}@example.com"));
        }
        Assert.Contains(HttpStatusCode.TooManyRequests, answers);
        await AssertTooManyAsync(gateway, $"another.{tag}@example.com");

        // An address of one client: one budget whatever account it types.
        const string single = "198.51.100.44";
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await AttemptAsync(single, $"one{i}.{tag}@example.com"));
        }
        await AssertTooManyAsync(single, $"one9.{tag}@example.com");
    }

    private async Task<HttpStatusCode> AttemptAsync(string clientAddress, string email)
    {
        using var client = fixture.Env.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-in") { Content = JsonContent.Create(new { email, password = "Wrong-Password-1" }) };
        request.Headers.Add("X-Forwarded-For", clientAddress);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private async Task AssertTooManyAsync(string clientAddress, string email)
    {
        using var client = fixture.Env.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-in") { Content = JsonContent.Create(new { email, password = "Wrong-Password-1" }) };
        request.Headers.Add("X-Forwarded-For", clientAddress);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("request.tooMany", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
