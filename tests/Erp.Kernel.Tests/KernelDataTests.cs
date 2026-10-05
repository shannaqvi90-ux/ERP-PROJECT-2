using System.Diagnostics;
using Erp.Kernel.Data;
using Erp.Kernel.Seeding;
using Erp.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace Erp.Kernel.Tests;

public sealed class KernelFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() =>
        Env = await ErpTestEnvironment.StartAsync(SeedPlan.Gate(ErpTestEnvironment.NewCanary(), ErpTestEnvironment.Password));

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>The unit-of-work session, the tenant guard, both isolation layers and bulk loading,
/// against a real PostgreSQL.</summary>
public sealed class KernelDataTests(KernelFixture fixture) : IClassFixture<KernelFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task Using_a_module_context_before_binding_a_tenant_fails_loudly()
    {
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KernelDbContext>();
        await Assert.ThrowsAsync<TenantContextMissingException>(() => db.AuditEntries.CountAsync());
    }

    [Fact]
    public async Task A_unit_of_work_cannot_switch_tenants()
    {
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.BeginAsync(Env.TenantA.Id, null, "system");
        await session.BeginAsync(Env.TenantA.Id, null, "system"); // same tenant: no-op
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.BeginAsync(Env.TenantB.Id, null, "system"));
    }

    [Fact]
    public async Task The_query_filter_is_a_second_layer_on_every_tenant_entity()
    {
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.BeginAsync(Env.TenantA.Id, null, "system");
        var db = scope.ServiceProvider.GetRequiredService<KernelDbContext>();
        var sql = db.AuditEntries.ToQueryString();
        Assert.Contains("tenant_id", sql, StringComparison.Ordinal);
        var tenants = await db.AuditEntries.Select(e => e.TenantId).Distinct().ToListAsync();
        Assert.Equal([Env.TenantA.Id], tenants);
    }

    [Fact]
    public async Task EF_refuses_to_write_a_row_for_another_tenant()
    {
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.BeginAsync(Env.TenantA.Id, null, "system");
        var db = scope.ServiceProvider.GetRequiredService<KernelDbContext>();
        db.AuditEntries.Add(new AuditEntry { TenantId = Env.TenantB.Id, ActorKind = "test", TableSchema = "x", TableName = "y", Action = "insert" });
        await Assert.ThrowsAsync<CrossTenantWriteException>(() => db.SaveChangesAsync());
    }

    [Fact]
    [Trait(TimingBudget.Trait, TimingBudget.Value)]
    public async Task Bulk_insert_loads_100000_rows_through_row_level_security_quickly()
    {
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.BeginAsync(Env.TenantA.Id, null, "seed");
        var columns = new BulkColumn[]
        {
            new("tenant_id", NpgsqlDbType.Uuid), new("actor_kind", NpgsqlDbType.Varchar), new("table_schema", NpgsqlDbType.Varchar),
            new("table_name", NpgsqlDbType.Varchar), new("action", NpgsqlDbType.Varchar), new("changes", NpgsqlDbType.Jsonb),
            new("transaction_id", NpgsqlDbType.Bigint),
        };
        var rows = Enumerable.Range(0, 100_000).Select(i => new object?[] { Env.TenantA.Id, "seed", "bulk", "test", "insert", "{}", (long)i });
        var stopwatch = Stopwatch.StartNew();
        var inserted = await BulkInsert.InsertAsync(session, "audit", "entries", columns, rows);
        stopwatch.Stop();
        await session.RollbackAsync();
        Assert.Equal(100_000, inserted);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"100,000 rows took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Bulk_insert_cannot_smuggle_rows_into_another_tenant()
    {
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.BeginAsync(Env.TenantA.Id, null, "seed");
        var columns = new BulkColumn[]
        {
            new("tenant_id", NpgsqlDbType.Uuid), new("actor_kind", NpgsqlDbType.Varchar), new("table_schema", NpgsqlDbType.Varchar),
            new("table_name", NpgsqlDbType.Varchar), new("action", NpgsqlDbType.Varchar), new("changes", NpgsqlDbType.Jsonb),
            new("transaction_id", NpgsqlDbType.Bigint),
        };
        var rows = new[]
        {
            new object?[] { Env.TenantA.Id, "seed", "bulk", "test", "insert", "{}", 1L },
            new object?[] { Env.TenantB.Id, "seed", "bulk", "test", "insert", "{}", 2L },
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => BulkInsert.InsertAsync(session, "audit", "entries", columns, rows));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task Bulk_insert_requires_a_bound_tenant()
    {
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await Assert.ThrowsAsync<TenantContextMissingException>(() =>
            BulkInsert.InsertAsync(session, "audit", "entries", [new("tenant_id", NpgsqlDbType.Uuid)], []));
    }

    [Fact]
    public async Task Migrations_and_seeding_are_idempotent()
    {
        await Env.BootstrapAsync();
        await Env.Factory.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync();
        await using var admin = await Env.OpenAdminAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM identity.users", admin);
        var before = (long)(await count.ExecuteScalarAsync())!;
        await Env.Factory.Services.GetRequiredService<SeedRunner>().RunAsync(Env.Plan);
        var after = (long)(await count.ExecuteScalarAsync())!;
        Assert.Equal(before, after);
    }
}
