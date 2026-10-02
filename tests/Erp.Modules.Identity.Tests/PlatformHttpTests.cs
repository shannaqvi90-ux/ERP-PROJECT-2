using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Erp.Modules.Identity.Tests;

/// <summary>Platform-level HTTP behaviour every module relies on.</summary>
public sealed class PlatformHttpTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Health_is_public_and_checks_the_database()
    {
        using var client = fixture.Env.CreateClient();
        var health = await client.GetFromJsonAsync<JsonElement>("/api/health");
        Assert.Equal("ok", health.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Unknown_api_paths_answer_404_problems_not_the_app_shell()
    {
        using var client = fixture.Env.CreateClient();
        var response = await client.GetAsync("/api/no/such/thing");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_api_responses_are_not_cached()
    {
        using var client = fixture.Env.CreateClient();
        var response = await client.GetAsync("/api/auth/session");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Server_errors_reveal_nothing_but_a_trace_id()
    {
        using var client = fixture.Env.CreateClient();
        var response = await client.PostAsync("/api/auth/sign-in", new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", text, StringComparison.Ordinal);
    }
}
