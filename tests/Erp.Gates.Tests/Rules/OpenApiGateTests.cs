using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

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
