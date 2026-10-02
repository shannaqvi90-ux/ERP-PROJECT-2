using Erp.Gates.Tests.G1;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.SelfTests;

public sealed class LeakyFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
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
