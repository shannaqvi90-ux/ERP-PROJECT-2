using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Tenancy.Access;

/// <summary>Where one user may work in one company: every branch (<see cref="Branches"/> null),
/// or only the listed branches.</summary>
internal sealed record CompanyHolding(Guid CompanyId, IReadOnlySet<Guid>? Branches)
{
    public bool AllBranches => Branches is null;

    /// <summary>True when this access covers <paramref name="other"/> (every branch covers
    /// anything; listed branches cover only a subset of themselves).</summary>
    public bool Covers(CompanyHolding? other) =>
        other is null || AllBranches || (!other.AllBranches && other.Branches!.IsSubsetOf(Branches!));

    public bool SameAs(CompanyHolding? other) =>
        other is not null && AllBranches == other.AllBranches && (AllBranches || Branches!.SetEquals(other.Branches!));
}

/// <summary>
/// Company and branch access is a grant, held to the same rule as roles: a caller gives and takes
/// only access they hold themselves, and only on users who hold no more than they do.
/// <list type="bullet">
/// <item>Nobody changes their own access.</item>
/// <item>The user may hold no permission the caller lacks (an access clerk cannot take an
/// administrator out of a company).</item>
/// <item>The user may work in no company outside the caller's own (an administrator of one company
/// cannot take the group's owner out of it).</item>
/// <item>In each company whose access changes, the caller's own access must cover the user's
/// access before and after the change (a one-branch manager gives only that branch, and cannot
/// narrow or remove someone who works in every branch).</item>
/// </list>
/// Creating a company, or changing a company's code, needs every company of the workspace: the
/// code is unique across the workspace, so a caller who does not see every company would learn
/// from a refusal that a company they cannot see uses it.
/// </summary>
internal static class CompanyAccessRules
{
    /// <summary>The user's access in every company of the caller's scope (row-level security
    /// shows no other rows), by company.</summary>
    public static async Task<Dictionary<Guid, CompanyHolding>> HoldingsAsync(TenancyDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var companies = await db.CompanyAccess.AsNoTracking().Where(a => a.UserId == userId)
            .Select(a => new { a.CompanyId, a.AllBranches }).ToListAsync(cancellationToken);
        var branches = await db.BranchAccess.AsNoTracking().Where(b => b.UserId == userId)
            .Select(b => new { b.CompanyId, b.BranchId }).ToListAsync(cancellationToken);
        return companies.ToDictionary(c => c.CompanyId, c => new CompanyHolding(c.CompanyId,
            c.AllBranches ? null : branches.Where(b => b.CompanyId == c.CompanyId).Select(b => b.BranchId).ToHashSet()));
    }

    /// <summary>
    /// The version of the user's access as the caller sees it: a hash over the user's company and
    /// branch access rows in the caller's scope, each with its row version (<c>xmin</c>). Any
    /// insert, change or delete of one of those rows changes it; rows in companies the caller cannot
    /// see do not take part, so it tells the caller nothing about them.
    /// </summary>
    public static async Task<uint> VersionAsync(TenancyDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var companies = await db.CompanyAccess.AsNoTracking().Where(a => a.UserId == userId)
            .Select(a => new { a.CompanyId, a.AllBranches, a.Version }).ToListAsync(cancellationToken);
        var branches = await db.BranchAccess.AsNoTracking().Where(b => b.UserId == userId)
            .Select(b => new { b.BranchId, b.Version }).ToListAsync(cancellationToken);
        var text = new StringBuilder();
        foreach (var c in companies.OrderBy(c => c.CompanyId))
        {
            text.Append('c').Append(c.CompanyId).Append(':').Append(c.AllBranches).Append(':').Append(c.Version).Append(';');
        }
        foreach (var b in branches.OrderBy(b => b.BranchId))
        {
            text.Append('b').Append(b.BranchId).Append(':').Append(b.Version).Append(';');
        }
        return BinaryPrimitives.ReadUInt32LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>
    /// Serialises changes to one user's access: the user's row of <c>user_company_totals</c> (made
    /// when missing; a second request making it at the same moment gets a unique violation, 409) is
    /// updated, which holds its row lock until the request's transaction ends. Rolled back with the
    /// request when it does not succeed.
    /// </summary>
    public static async Task LockAsync(TenancyDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var touched = await db.CompanyTotals.Where(t => t.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
        if (touched == 0)
        {
            db.CompanyTotals.Add(new UserCompanyTotal { UserId = userId, CompanyCount = 0 });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>How many companies the user may work in, in the whole workspace.</summary>
    public static async Task<int> TotalCompaniesAsync(TenancyDbContext db, Guid userId, CancellationToken cancellationToken) =>
        await db.CompanyTotals.AsNoTracking().Where(t => t.UserId == userId).Select(t => t.CompanyCount).SingleOrDefaultAsync(cancellationToken);

    /// <summary>True when the unit of work's company scope is every company of the workspace.</summary>
    public static async Task<bool> ScopeHoldsEveryCompanyAsync(TenancyDbContext db, ICompanyContext scope, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (scope.AllCompanies)
        {
            return true;
        }
        var count = await db.Tenants.AsNoTracking().Where(t => t.Id == tenant.TenantId).Select(t => t.CompanyCount).SingleOrDefaultAsync(cancellationToken);
        return scope.CompanyIds.Count >= count;
    }

    /// <summary>The problem key when the caller may not act on this user at all, or null.</summary>
    public static async Task<string?> RefuseUserAsync(TenancyDbContext db, Guid callerId, Func<string, bool> callerHas, Guid userId,
        IReadOnlySet<string> userPermissions, int visibleCompanies, CancellationToken cancellationToken)
    {
        if (userId == callerId)
        {
            return "tenancy.cannotChangeOwnAccess";
        }
        if (!userPermissions.All(callerHas))
        {
            return "tenancy.userBeyondOwn";
        }
        if (await TotalCompaniesAsync(db, userId, cancellationToken) > visibleCompanies)
        {
            return "tenancy.userBeyondOwnCompanies";
        }
        // Nor in a branch the caller does not (critic p02 round 6: a one-branch administrator was
        // offered the access of a user who also holds another branch, which it could not see, and
        // every save, even an unchanged one, was refused because the hidden branch read as removed).
        var mine = await HoldingsAsync(db, callerId, cancellationToken);
        if ((await HoldingsAsync(db, userId, cancellationToken)).Values.Any(h => mine.GetValueOrDefault(h.CompanyId) is not { } own || !own.Covers(h)))
        {
            return "tenancy.userBeyondOwnBranches";
        }
        return null;
    }

    /// <summary>True when every company whose access changes from <paramref name="before"/> to
    /// <paramref name="after"/> is one where the caller's access covers both.</summary>
    public static bool ChangeWithinCaller(IReadOnlyDictionary<Guid, CompanyHolding> caller, IReadOnlyDictionary<Guid, CompanyHolding> before,
        IReadOnlyDictionary<Guid, CompanyHolding> after)
    {
        foreach (var company in before.Keys.Union(after.Keys))
        {
            var old = before.GetValueOrDefault(company);
            var wanted = after.GetValueOrDefault(company);
            if (old is null ? wanted is null : old.SameAs(wanted))
            {
                continue;
            }
            if (caller.GetValueOrDefault(company) is not { } mine || !mine.Covers(old) || !mine.Covers(wanted))
            {
                return false;
            }
        }
        return true;
    }
}
