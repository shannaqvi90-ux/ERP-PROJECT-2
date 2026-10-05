using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Erp.Kernel.Seeding;

/// <summary>Runs every module's seeders for each tenant of a plan. Each tenant is seeded in its
/// own scope and transaction, bound to that tenant, as the application role: row-level security
/// and the audit trigger apply to seed data exactly as to user data. Seeding runs on the bulk
/// pool (<c>ErpDataSources.BuildBulk</c>): every statement a seeder sends, however it
/// creates it, has the long bulk command timeout, so demo volume loads on a saturated machine.</summary>
public sealed class SeedRunner(IServiceProvider services, ModuleCatalog catalog, IConfiguration configuration, ILogger<SeedRunner> logger)
{
    public async Task RunAsync(SeedPlan plan, CancellationToken cancellationToken = default)
    {
        var seederTypes = catalog.Modules.SelectMany(m => m.Seeders).ToList();
        await using var bulk = ErpDataSources.BuildBulk(configuration, services.GetServices<IDataSourceObserver>());
        foreach (var tenant in plan.Tenants)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            await using var scope = services.CreateAsyncScope();
            var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
            await session.UseBulkConnectionAsync(bulk);
            session.CorrelationId = $"seed:{plan.Profile.ToString().ToLowerInvariant()}";
            await session.BeginAsync(tenant.Id, null, "seed", cancellationToken);
            var context = new TenantSeedContext(plan, tenant, scope.ServiceProvider);
            var seeders = seederTypes
                .Select(t => (ITenantSeeder)scope.ServiceProvider.GetRequiredService(t))
                .OrderBy(s => s.Order)
                .ThenBy(s => s.GetType().FullName, StringComparer.Ordinal);
            foreach (var seeder in seeders)
            {
                await seeder.SeedAsync(context, cancellationToken);
            }
            await session.CommitAsync(cancellationToken);
            logger.LogInformation("Seeded tenant {Tenant} ({Profile}) in {Elapsed} ms", tenant.Code, plan.Profile, started.ElapsedMilliseconds);
        }
    }
}
