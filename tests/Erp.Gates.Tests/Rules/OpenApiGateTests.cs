using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.Rules;

/// <summary>Every API endpoint appears in the OpenAPI document generated from the running app,
/// with a summary, an operation id, and its permission (or anonymous reason).</summary>
public sealed class OpenApiGateTests(GateFixture fixture)
{
    [Fact]
    public async Task OpenApi_document_covers_every_endpoint_with_its_permission()
    {
        using var client = fixture.Env.CreateClient();
        var document = await OpenApiDocument.LoadAsync(client);
        var problems = new List<string>();
        var covered = 0;
        foreach (var endpoint in EndpointInventory.From(fixture.Env.Factory.Services).Where(e => e.InOpenApi))
        {
            if (!document.TryGetOperation(endpoint.Method, endpoint.Pattern, out var operation))
            {
                problems.Add($"{endpoint}: missing from OpenAPI");
                continue;
            }
            covered++;
            if (!operation.TryGetProperty("summary", out var summary) || string.IsNullOrWhiteSpace(summary.GetString()))
            {
                problems.Add($"{endpoint}: no summary");
            }
            if (!operation.TryGetProperty("operationId", out _))
            {
                problems.Add($"{endpoint}: no operationId (use WithName)");
            }
            if (endpoint.IsAnonymous)
            {
                if (!operation.TryGetProperty("x-erp-anonymous", out _)) problems.Add($"{endpoint}: anonymous reason not documented");
            }
            else if (!operation.TryGetProperty("x-erp-permission", out var permission) || permission.GetString() != endpoint.Permission)
            {
                problems.Add($"{endpoint}: x-erp-permission must be {endpoint.Permission}");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(covered >= Ratchet.Min("rules.openApiOperations"), $"{covered} operations documented");
    }

    [Fact]
    public async Task The_document_is_the_same_for_every_caller_and_never_repeats_the_host_it_was_asked_on()
    {
        // The framework put the request's Host header in the document's server list, so the
        // description echoed what any caller sent. It now depends only on the code and is generated
        // once per process (ApiDescriptionDocument).
        using var anonymous = fixture.Env.CreateClient();
        using var signedIn = await fixture.Env.SignInAsync(fixture.Env.Email(fixture.Env.Plan.Tenants[0], "admin"));
        var plain = await anonymous.GetAsync("/api/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, plain.StatusCode);
        Assert.Equal("application/json", plain.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", plain.Content.Headers.ContentType?.CharSet);
        var text = await plain.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var other = new HttpRequestMessage(HttpMethod.Get, "/api/openapi/v1.json");
        other.Headers.Host = "caller-host.example:8443";
        other.Headers.Add("X-Forwarded-Host", "forwarded-host.example");
        other.Headers.Add("X-Forwarded-Proto", "https");
        other.Headers.Add("X-Forwarded-Prefix", "/forwarded-prefix");
        var otherText = await (await signedIn.SendAsync(other, TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var arabic = new HttpRequestMessage(HttpMethod.Get, "/api/openapi/v1.json");
        arabic.Headers.AcceptLanguage.ParseAdd("ar-AE");
        var arabicText = await (await anonymous.SendAsync(arabic, TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(text == otherText, "the document differs with the caller, its Host or its forwarded headers");
        Assert.True(text == arabicText, "the document differs with the caller's language");
        foreach (var echoed in new[] { "caller-host.example", "forwarded-host.example", "forwarded-prefix", "http://localhost" })
        {
            Assert.DoesNotContain(echoed, otherText, StringComparison.OrdinalIgnoreCase);
        }
        using var document = JsonDocument.Parse(text);
        Assert.False(document.RootElement.TryGetProperty("servers", out var servers) && servers.GetArrayLength() > 0, "the document names a server URL");
        // The same text the process holds: generated once, not per request.
        var held = fixture.Env.Factory.Services.GetRequiredService<Erp.Kernel.Hosting.ApiDescriptionDocument>();
        Assert.Same(held, fixture.Env.Factory.Services.GetRequiredService<Erp.Kernel.Hosting.ApiDescriptionDocument>());
        Assert.Equal(held.Json, text);
    }

    [Fact]
    public async Task Decimals_are_documented_as_strings()
    {
        using var client = fixture.Env.CreateClient();
        var document = await OpenApiDocument.LoadAsync(client);
        var json = document.Root.GetRawText();
        Assert.DoesNotContain("\"format\":\"double\"", json.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("\"format\":\"float\"", json.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
        _ = JsonDocument.Parse(json);
    }
}
