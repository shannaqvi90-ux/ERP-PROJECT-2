using Erp.Kernel.Seeding;
using Erp.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Tenancy.Seeding;

/// <summary>
/// After the users exist (identity seeds at order 10): which companies and branches the named
/// users may work in, and where they start. Administrators work in every company; the read-only
/// user in the first company only, limited to some of its branches; the user without roles in
/// none. In the gate fixture the second administrator is limited to one branch of the second
/// company and starts there, so every access table holds rows of both companies. A provisioned
/// workspace's first administrator works in its company.
/// </summary>
internal sealed class TenancyAccessSeeder(TenancyDbContext db, IUserDirectory users) : ITenantSeeder
{
    public int Order => 20;

    public async Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (await db.CompanyAccess.AnyAsync(cancellationToken))
        {
            return;
        }
        var companies = await db.Companies.AsNoTracking().OrderBy(c => c.Id).Select(c => c.Id).ToListAsync(cancellationToken);
        var branches = await db.Branches.AsNoTracking().OrderBy(b => b.Id).Select(b => new { b.Id, b.CompanyId }).ToListAsync(cancellationToken);
        if (companies.Count == 0)
        {
            return;
        }
        List<Guid> BranchesOf(Guid company) => branches.Where(b => b.CompanyId == company).Select(b => b.Id).ToList();

        async Task<Guid?> UserAsync(string email) => (await users.FindByEmailAsync(email, cancellationToken))?.Id;

        void Grant(Guid user, Guid company, IReadOnlyList<Guid>? onlyBranches = null)
        {
            db.CompanyAccess.Add(new UserCompanyAccess { UserId = user, CompanyId = company, AllBranches = onlyBranches is null });
            foreach (var branch in onlyBranches ?? [])
            {
                db.BranchAccess.Add(new UserBranchAccess { UserId = user, CompanyId = company, BranchId = branch });
            }
        }

        void Start(Guid user, Guid company, Guid? branch) =>
            db.Workplaces.Add(new UserWorkplace { UserId = user, CompanyId = company, BranchId = branch });

        if (context.Plan.Profile == SeedProfile.Provision)
        {
            if (context.Tenant.Administrator is { } administrator && await UserAsync(administrator.Email) is { } owner)
            {
                Grant(owner, companies[0]);
                Start(owner, companies[0], BranchesOf(companies[0]).FirstOrDefault());
            }
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var domain = context.Tenant.EmailDomain;
        var first = companies[0];
        var last = companies[^1];
        if (await UserAsync($"admin@{domain}") is { } admin)
        {
            companies.ForEach(c => Grant(admin, c));
            Start(admin, first, BranchesOf(first).FirstOrDefault());
        }
        if (await UserAsync($"admin.ar@{domain}") is { } adminArabic)
        {
            foreach (var company in companies)
            {
                Grant(adminArabic, company, context.Plan.Profile == SeedProfile.Gate && company == last ? BranchesOf(company).Take(1).ToList() : null);
            }
            var startIn = context.Plan.Profile == SeedProfile.Gate ? last : first;
            Start(adminArabic, startIn, BranchesOf(startIn).FirstOrDefault());
        }
        if (await UserAsync($"viewer@{domain}") is { } viewer)
        {
            var some = BranchesOf(first);
            Grant(viewer, first, some.Count > 1 ? some.Take(some.Count - 1).ToList() : null);
            Start(viewer, first, some.FirstOrDefault());
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
