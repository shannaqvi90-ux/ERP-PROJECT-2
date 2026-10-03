using System.Collections.Concurrent;
using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Seeding;
using Erp.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Kernel.Tests;

/// <summary>
/// A test module whose seeder runs one statement longer than Npgsql's 30-second default command
/// timeout (critic p00 round 3: the 100,000-credential seed statement timed out at that default on
/// a saturated machine and ./erp verify failed). It records what the seeding session looked like.
/// </summary>
public sealed class SlowSeedModule : ErpModule
{
    public const string SleepSetting = "Erp:Testing:SlowSeedSeconds";

    /// <summary>Per tenant code: the command timeout of the seeding session and how long the slow
    /// statement ran.</summary>
    public static readonly ConcurrentDictionary<string, (int Timeout, TimeSpan Ran)> Observed = new();

    public override string Name => "slowseed";

    public override void Register(ModuleBuilder module) => module.Seeder<SlowSeeder>();

    internal sealed class SlowSeeder(ErpDbSession session, IConfiguration configuration) : ITenantSeeder
    {
        public int Order => 1000;

        public async Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken)
        {
            var seconds = context.Tenant.Id == context.Plan.Tenants[0].Id
                ? int.Parse(configuration[SleepSetting] ?? "0", System.Globalization.CultureInfo.InvariantCulture)
                : 0;
            var started = System.Diagnostics.Stopwatch.StartNew();
            // Created the way any seeder creates a command: no timeout of its own.
            await using (var command = new NpgsqlCommand("SELECT pg_sleep(@seconds)", session.Connection, session.Transaction))
            {
                command.Parameters.AddWithValue("seconds", (double)seconds);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            Observed[context.Tenant.Code] = (session.CommandTimeoutSeconds, started.Elapsed);
        }
    }
}

/// <summary>An environment whose seeding includes <see cref="SlowSeedModule"/>'s 35-second statement.</summary>
public sealed class SlowSeedFixture : IAsyncLifetime
{
    public const int SleepSeconds = 35;

    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() =>
        Env = await ErpTestEnvironment.StartAsync(SeedPlan.Gate(ErpTestEnvironment.NewCanary(), ErpTestEnvironment.Password), new Dictionary<string, string?>
        {
            ["Erp:Testing:ExtraModules"] = typeof(SlowSeedModule).AssemblyQualifiedName,
            [SlowSeedModule.SleepSetting] = SleepSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>Bulk work (seeding, imports) runs with a long command timeout on every statement, set
/// once on its connection pool, so a statement that takes minutes on a busy machine still
/// finishes; requests keep the ordinary short timeout.</summary>
public sealed class BulkTimeoutTests(SlowSeedFixture fixture) : IClassFixture<SlowSeedFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private IConfiguration Configuration => Env.Factory.Services.GetRequiredService<IConfiguration>();

    [Fact]
    public void A_seed_statement_longer_than_the_default_timeout_finishes()
    {
        var (timeout, ran) = SlowSeedModule.Observed[Env.TenantA.Code];
        Assert.True(ran >= TimeSpan.FromSeconds(SlowSeedFixture.SleepSeconds - 1), $"the slow statement ran {ran}");
        Assert.Equal(ErpDataSources.DefaultBulkCommandTimeoutSeconds, timeout);
        Assert.True(SlowSeedModule.Observed.ContainsKey(Env.TenantB.Code), "every tenant was seeded after the slow statement");
    }

    [Fact]
    public async Task Request_units_of_work_keep_the_short_default_timeout()
    {
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        Assert.Equal(30, session.CommandTimeoutSeconds);
    }

    [Fact]
    public async Task Every_command_on_a_bulk_unit_of_work_inherits_the_long_timeout()
    {
        await using var bulk = ErpDataSources.BuildBulk(Configuration);
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.UseBulkConnectionAsync(bulk);
        await session.BeginAsync(Env.TenantA.Id, null, "seed");
        Assert.Equal(ErpDataSources.DefaultBulkCommandTimeoutSeconds, session.CommandTimeoutSeconds);

        // A command created with no timeout of its own.
        await using var command = new NpgsqlCommand("SELECT 1", session.Connection, session.Transaction);
        Assert.Equal(ErpDataSources.DefaultBulkCommandTimeoutSeconds, command.CommandTimeout);
        // The command an EF context in the same scope sends.
        var db = scope.ServiceProvider.GetRequiredService<KernelDbContext>();
        await using var efCommand = Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetDbConnection(db.Database).CreateCommand();
        Assert.Equal(ErpDataSources.DefaultBulkCommandTimeoutSeconds, efCommand.CommandTimeout);
        Assert.Null(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetCommandTimeout(db.Database));
        // The bulk loader's own statements and COPY take the session's timeout.
        Assert.True(BulkInsert.TimeoutSeconds(session) >= ErpDataSources.DefaultBulkCommandTimeoutSeconds);
    }

    [Fact]
    public async Task The_bulk_loader_never_runs_a_statement_under_the_bulk_minimum_even_on_a_request_connection()
    {
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.BeginAsync(Env.TenantA.Id, null, "system");
        Assert.Equal(ErpDataSources.MinimumBulkCommandTimeoutSeconds, BulkInsert.TimeoutSeconds(session));
    }

    [Fact]
    public async Task The_bulk_connection_cannot_be_chosen_after_the_unit_of_work_used_its_connection()
    {
        await using var bulk = ErpDataSources.BuildBulk(Configuration);
        await using (var scope = Env.Factory.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
            _ = scope.ServiceProvider.GetRequiredService<KernelDbContext>().Model; // a context took the connection
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.UseBulkConnectionAsync(bulk));
        }
        await using (var scope = Env.Factory.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
            await session.BeginAsync(Env.TenantA.Id, null, "system");
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.UseBulkConnectionAsync(bulk));
        }
    }

    [Theory]
    [InlineData("30")]
    [InlineData("599")]
    [InlineData("-1")]
    [InlineData("ten minutes")]
    public void A_bulk_timeout_below_the_minimum_or_unreadable_is_refused(string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ErpDataSources.BulkCommandTimeoutSetting] = value,
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => ErpDataSources.BulkCommandTimeoutSeconds(configuration));
        Assert.Contains(ErpDataSources.BulkCommandTimeoutSetting, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_configured_bulk_timeout_is_used_and_an_unset_one_defaults_to_an_hour()
    {
        Assert.Equal(ErpDataSources.DefaultBulkCommandTimeoutSeconds,
            ErpDataSources.BulkCommandTimeoutSeconds(new ConfigurationBuilder().Build()));
        var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ErpDataSources.BulkCommandTimeoutSetting] = "7200",
        }).Build();
        Assert.Equal(7200, ErpDataSources.BulkCommandTimeoutSeconds(configured));
    }
}
