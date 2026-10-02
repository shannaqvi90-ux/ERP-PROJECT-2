using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

public sealed class RateLimitFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() =>
        Env = await ErpTestEnvironment.StartGateAsync(new Dictionary<string, string?> { ["Erp:RateLimits:SignInPerMinute"] = "3" });

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
}
