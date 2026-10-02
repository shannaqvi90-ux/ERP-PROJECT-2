using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Shell;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Tenancy;

/// <summary>Tenants (and, from p02, their companies and branches).</summary>
public sealed class TenancyModule : ErpModule
{
    public override string Name => "tenancy";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions(TenancyPermissions.TenantRead, TenancyPermissions.TenantUpdate);
        module.DbContext<TenancyDbContext>();
        module.Services.AddScoped<ITenantDirectory, TenantDirectory>();
        module.Endpoints(TenancyEndpoints.Map);
        module.Menu(new MenuEntry("tenancy.tenant", "tenancy.menu.tenant", "/tenancy/tenant", TenancyPermissions.TenantRead, Order: 900, Group: "settings"));
        module.Seeder<TenancySeeder>();
    }
}

/// <summary>A customer workspace. Its <c>tenant_id</c> always equals its own id (check
/// constraint), so the same row-level security policy that guards every other table guards this
/// one.</summary>
public sealed class Tenant : TenantEntity
{
    public string Code { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string Status { get; set; } = TenantStatus.Active;
}

public static class TenantStatus
{
    public const string Active = "active";
    public const string Suspended = "suspended";
}

public sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options, ITenantContext? tenant = null)
    : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "tenancy";

    protected override string Schema => SchemaName;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants", t =>
            {
                t.HasCheckConstraint("ck_tenants_status", "status IN ('active', 'suspended')");
                t.HasCheckConstraint("ck_tenants_tenant_is_self", "tenant_id = id");
                t.HasCheckConstraint("ck_tenants_code", "code ~ '^[a-z0-9][a-z0-9-]{1,39}$'");
            });
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => x.Code).IsUnique();
        });
    }
}

internal sealed class TenancyDbContextDesignFactory : IDesignTimeDbContextFactory<TenancyDbContext>
{
    public TenancyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenancyDbContext>();
        DataRegistration.ConfigureNpgsql(options, "Host=localhost;Database=design", TenancyDbContext.SchemaName);
        return new TenancyDbContext(options.Options);
    }
}

internal sealed class TenantDirectory(TenancyDbContext db, ITenantContext tenant) : ITenantDirectory
{
    public async Task<TenantInfo?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        if (!tenant.HasTenant)
        {
            return null;
        }
        return await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenant.TenantId && t.Status == TenantStatus.Active)
            .Select(t => new TenantInfo(t.Id, t.Code, t.NameEn, t.NameAr, t.Status))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
