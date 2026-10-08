using Erp.Testing;

[assembly: AssemblyFixture(typeof(Erp.Gates.Tests.Infrastructure.GateFixture))]

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// One gate environment for the read-only gate checks: tenant A (alpha) and tenant B (bravo,
/// every text field carrying a canary). Tests that change data start their own environment.
/// </summary>
public sealed class GateFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Env = await ErpTestEnvironment.StartGateAsync();
        await GatePreparation.PrepareAsync(Env);
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

public static class GatePreparation
{
    /// <summary>Sign in users of both tenants so every tenant table (sessions included) holds
    /// rows of both tenants.</summary>
    public static async Task PrepareAsync(ErpTestEnvironment env)
    {
        foreach (var tenant in env.Plan.Tenants)
        {
            foreach (var local in new[] { "admin", "viewer" })
            {
                using var client = await env.SignInAsync(env.Email(tenant, local));
            }
        }
    }
}
