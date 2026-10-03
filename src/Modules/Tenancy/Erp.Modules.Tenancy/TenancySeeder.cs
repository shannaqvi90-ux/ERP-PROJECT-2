using Erp.Kernel.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Tenancy;

internal sealed class TenancySeeder(TenancyDbContext db) : ITenantSeeder
{
    public int Order => 0;

    public async Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (await db.Tenants.AnyAsync(cancellationToken))
        {
            return;
        }
        db.Tenants.Add(new Tenant
        {
            Id = context.Tenant.Id,
            Code = context.Tenant.Code,
            NameEn = context.Tenant.NameEn,
            NameAr = context.Tenant.NameAr,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
