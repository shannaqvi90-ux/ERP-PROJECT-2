using Erp.Gates.Tests.G1;
using Erp.Gates.Tests.G2;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.SelfTests;

public sealed class LeakyFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        SqlTrace.EnsureStarted();
        Env = await ErpTestEnvironment.StartGateAsync(new Dictionary<string, string?>
        {
            ["Erp:Testing:ExtraModules"] = typeof(LeakyModule).AssemblyQualifiedName,
        });
        await GatePreparation.PrepareAsync(Env);
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// The gates must fail meaningfully. These tests plant isolation bugs and require the gate
/// machinery to report each one, so a gate that silently stopped looking would be caught.
/// </summary>
public sealed class GateSelfTests(LeakyFixture fixture) : IClassFixture<LeakyFixture>
{
    [Fact]
    public async Task The_HTTP_attack_catches_planted_header_route_and_body_leaks()
    {
        var report = await IsolationAttack.RunAsync(fixture.Env);
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/by-header", StringComparison.Ordinal) && l.Contains("[TenantHeaders]", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/tenants/", StringComparison.Ordinal));
        Assert.Contains("tenancy.tenants", report.ChangedTables);
        Assert.DoesNotContain(report.Leaks, l => !l.Contains("/api/leaky/", StringComparison.Ordinal));

        // Lookups by e-mail, by an undocumented-name query parameter and by a body text field.
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/lookup?email=", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/report?ownerReference=", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/find [body reference=", StringComparison.Ordinal));

        // An endpoint that only says whether a tenant B e-mail exists.
        Assert.Contains(report.Oracles, o => o.Contains("/api/leaky/exists?email=", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Oracles, o => !o.Contains("/api/leaky/", StringComparison.Ordinal));

        // The reviewed sign-in lookup reused by other endpoints.
        foreach (var name in new[] { "leaky.lookup", "leaky.exists", "leaky.find" })
        {
            Assert.Contains(report.LookupMisuse, m => m.Contains($"endpoint:{name}", StringComparison.Ordinal));
        }
        Assert.DoesNotContain(report.LookupMisuse, m => !m.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.Empty(report.UntracedFunctions);

        // Process-wide state: tenant B's activity fills a static cache and a singleton, and the
        // attack sees tenant B's data in tenant A's answers; the singleton also hands tenant A's
        // data back to tenant B.
        Assert.Contains(report.Leaks, l => l.Contains("GET /api/leaky/cached-tenant", StringComparison.Ordinal) && l.StartsWith("tenant A", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.Contains("GET /api/leaky/recent", StringComparison.Ordinal) && l.StartsWith("tenant A", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.Contains("GET /api/leaky/recent", StringComparison.Ordinal) && l.Contains("attacking tenant's marker", StringComparison.Ordinal));
        Assert.True(report.VictimConcurrentRequests > 0, "tenant B never read concurrently with the attack");
        Assert.Empty(report.VictimBlindSpots);
    }

    [Fact]
    public async Task The_grant_escalation_check_catches_an_endpoint_that_grants_any_role()
    {
        var result = await GrantEscalation.RunAsync(fixture.Env);
        Assert.Contains(result.Problems, p => p.StartsWith("POST /api/leaky/grants", StringComparison.Ordinal) && p.Contains("expected 403", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith("POST /api/leaky/grants", StringComparison.Ordinal) && p.Contains("Administrator", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.Contains("POST /api/identity/users", result.Checked);
    }

    [Fact]
    public async Task The_database_check_catches_a_table_without_row_level_security()
    {
        await using (var owner = new NpgsqlConnection(fixture.Env.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await DbCatalog.ExecuteAsync(owner, "CREATE TABLE tenancy.selftest_unprotected (id uuid PRIMARY KEY, tenant_id uuid NOT NULL)");
        }
        try
        {
            var (problems, _) = await G1DatabaseIsolationTests.RowLevelSecurityProblemsAsync(fixture.Env);
            Assert.Contains(problems, p => p.Contains("tenancy.selftest_unprotected: row-level security is not enabled", StringComparison.Ordinal));
            Assert.Contains(problems, p => p.Contains("tenancy.selftest_unprotected: needs exactly one tenant_isolation policy", StringComparison.Ordinal));
        }
        finally
        {
            await using var owner = new NpgsqlConnection(fixture.Env.OwnerConnectionString);
            await owner.OpenAsync();
            await DbCatalog.ExecuteAsync(owner, "DROP TABLE tenancy.selftest_unprotected");
        }
    }
}
