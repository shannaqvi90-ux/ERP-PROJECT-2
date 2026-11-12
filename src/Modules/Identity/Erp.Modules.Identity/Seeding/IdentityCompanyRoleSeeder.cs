using Erp.Kernel.Modules;
using Erp.Kernel.Seeding;
using Erp.Modules.Identity.Contracts;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Seeding;

/// <summary>
/// After companies and company access exist (tenancy seeds access at order 20): roles held in one
/// company. The accountant manages the first company (by id) and only reads the second, so the
/// demo shows a user whose permissions change with the working company, and every tenant of the
/// gate fixture holds company roles in two companies (the company-scope gate needs rows of both).
/// </summary>
internal sealed class IdentityCompanyRoleSeeder(IdentityDbContext db, ModuleCatalog catalog, ICompanyDirectory companies) : ITenantSeeder
{
    public int Order => 30;

    public async Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (context.Plan.Profile == SeedProfile.Provision || await db.UserCompanyRoles.AnyAsync(cancellationToken))
        {
            return;
        }
        var accountant = await db.Users.Where(u => u.EmailNormalized == $"accountant@{context.Tenant.EmailDomain}".ToLowerInvariant())
            .Select(u => (Guid?)u.Id).SingleOrDefaultAsync(cancellationToken);
        var inOrder = (await companies.ListAsync(cancellationToken)).OrderBy(c => c.Id).Take(2).ToList();
        if (accountant is not { } user || inOrder.Count == 0)
        {
            return;
        }
        var all = catalog.PermissionKeys.ToList();
        var manager = new Role
        {
            NameEn = context.Mark("Company manager"),
            NameAr = context.Mark("مدير الشركة"),
            // The company's records and its people's access, nothing workspace-wide.
            Permissions = all.Where(p => p.StartsWith("tenancy.companies.", StringComparison.Ordinal) || p.StartsWith("tenancy.branches.", StringComparison.Ordinal) ||
                                         p.StartsWith("tenancy.access.", StringComparison.Ordinal) || p == IdentityPermissions.UsersRead || p == IdentityPermissions.RolesRead)
                .Where(p => p != "tenancy.companies.create").Order(StringComparer.Ordinal).ToList(),
        };
        db.Roles.Add(manager);
        db.UserCompanyRoles.Add(new UserCompanyRole { UserId = user, RoleId = manager.Id, CompanyId = inOrder[0].Id });
        var readOnly = await db.Roles.Where(r => r.NameEn == context.Mark("Read-only")).Select(r => (Guid?)r.Id).SingleOrDefaultAsync(cancellationToken);
        if (readOnly is { } reader && inOrder.Count > 1)
        {
            db.UserCompanyRoles.Add(new UserCompanyRole { UserId = user, RoleId = reader, CompanyId = inOrder[1].Id });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
