using Erp.Modules.Tenancy.Access;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Tenancy.Workplace;

/// <summary>
/// An administrator chooses the company another user works in by default (their working company
/// until they switch). Everything is read within the caller's company scope: the user's access
/// rows, and their workplace row, are visible only in the caller's companies. A user who also
/// works in companies outside that scope is left alone (their default may be one of those).
/// The branch is cleared when the company changes: the session then picks the user's first branch
/// there, as it does for a company chosen in the top bar without a branch.
/// </summary>
internal sealed class UserWorkplaces(TenancyDbContext db) : IUserWorkplaces
{
    public async Task<UserWorkplaceInfo> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (companies, elsewhere) = await CompaniesAsync(userId, cancellationToken);
        var row = await db.Workplaces.AsNoTracking().Where(w => w.UserId == userId).Select(w => new { w.CompanyId, w.Version }).SingleOrDefaultAsync(cancellationToken);
        return new UserWorkplaceInfo(userId, row?.CompanyId, companies, elsewhere, row?.Version ?? 0);
    }

    public async Task<WorkplaceChange> SetAsync(Guid userId, Guid? companyId, uint version, CancellationToken cancellationToken)
    {
        var (companies, elsewhere) = await CompaniesAsync(userId, cancellationToken);
        if (elsewhere)
        {
            return WorkplaceChange.UserBeyondScope;
        }
        if (companyId is { } wanted && companies.All(c => c.Id != wanted || !c.IsActive))
        {
            return WorkplaceChange.CompanyNotAllowed;
        }
        var row = await db.Workplaces.SingleOrDefaultAsync(w => w.UserId == userId, cancellationToken);
        if ((row?.Version ?? 0) != version)
        {
            return WorkplaceChange.Stale;
        }
        if (row is not null)
        {
            db.Entry(row).Property(r => r.Version).OriginalValue = version;
        }
        if (companyId is not { } company)
        {
            if (row is not null)
            {
                db.Workplaces.Remove(row);
            }
        }
        else if (row is null)
        {
            db.Workplaces.Add(new UserWorkplace { UserId = userId, CompanyId = company, BranchId = null });
        }
        else if (row.CompanyId != company)
        {
            row.CompanyId = company;
            row.BranchId = null;
        }
        await db.SaveChangesAsync(cancellationToken);
        return WorkplaceChange.Done;
    }

    /// <summary>The companies of the caller's scope the user may work in (by code), and whether they
    /// work in any company outside it.</summary>
    private async Task<(IReadOnlyList<CompanyInfo> Companies, bool Elsewhere)> CompaniesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var companies = await (from access in db.CompanyAccess.AsNoTracking()
                               where access.UserId == userId
                               join c in db.Companies.AsNoTracking() on access.CompanyId equals c.Id
                               orderby c.Code
                               select new CompanyInfo(c.Id, c.Code, c.LegalNameEn, c.LegalNameAr, c.BaseCurrency, c.FiscalYearStartMonth, c.FiscalYearStartDay, c.Country, c.IsActive))
            .ToListAsync(cancellationToken);
        var total = await CompanyAccessRules.TotalCompaniesAsync(db, userId, cancellationToken);
        return (companies, total > companies.Count);
    }
}
