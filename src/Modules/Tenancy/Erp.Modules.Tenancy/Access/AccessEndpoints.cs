using Erp.Kernel.Http;
using Erp.Kernel.Lists;
using Erp.Kernel.Security;
using Erp.Modules.Identity.Contracts;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Tenancy.Access;

/// <summary>A company a user may work in, in a row of the access list.</summary>
public sealed record AccessCompanySummary(Guid CompanyId, string Code, bool AllBranches, int BranchCount);

/// <summary>A row of the access list: a user and the companies (of the caller's) they may work in.</summary>
public sealed record AccessRow(Guid Id, string DisplayName, string Email, IReadOnlyList<AccessCompanySummary> Companies);

/// <summary>Access to one company: every branch, or only <c>branchIds</c>.</summary>
public sealed record CompanyAccessDto(Guid CompanyId, bool AllBranches, IReadOnlyList<Guid> BranchIds);

public sealed record AccessBranchOption(Guid Id, string Code, string NameEn, string NameAr, bool IsActive);

/// <summary>A company the caller may give access to, with the branches the caller may give
/// (only their own branches when they are limited to some: <c>canGiveAllBranches</c> false).</summary>
public sealed record AccessCompanyOption(Guid Id, string Code, string LegalNameEn, string LegalNameAr, bool IsActive, IReadOnlyList<AccessBranchOption> Branches,
    bool CanGiveAllBranches = true);

/// <summary>Where one user may work, within the companies the caller may work in, and every
/// company and branch the caller could give them (<c>options</c>). <c>canEdit</c> is false, with
/// the reason as a text key in <c>readOnlyReason</c>, when the caller may not change this user's
/// access at all (their own access, a user holding permissions the caller lacks, or one who works
/// in companies the caller does not).</summary>
public sealed record UserAccessDto(Guid UserId, string DisplayName, string Email, bool IsCaller, IReadOnlyList<CompanyAccessDto> Companies,
    IReadOnlyList<AccessCompanyOption> Options, bool CanEdit = true, string? ReadOnlyReason = null);

public sealed record CompanyAccessRequest(Guid? CompanyId, bool? AllBranches, IReadOnlyList<Guid>? BranchIds);

/// <summary>The full set of the caller's companies the user may work in; companies the caller
/// cannot see are left as they are.</summary>
public sealed record UpdateAccessRequest(IReadOnlyList<CompanyAccessRequest>? Companies);

internal static class AccessEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/access", List)
            .WithName("tenancy.access.list")
            .WithSummary("Users of the workspace, each with the caller's companies they may work in, a page at a time: word search on name and e-mail, filters, sort, keyset or offset paging (the list query contract); by name by default.")
            .RequirePermission(TenancyPermissions.AccessRead);

        group.MapGet("/access/{userId:guid}", Get)
            .WithName("tenancy.access.get")
            .WithSummary("The companies and branches (of the caller's) one user may work in.")
            .RequirePermission(TenancyPermissions.AccessRead);

        group.MapPut("/access/{userId:guid}", Update)
            .WithName("tenancy.access.update")
            .WithSummary("Set the companies and branches one user may work in. Access is a grant: only companies and branches the caller works in can be given or taken away, only from users who hold no permission the caller lacks and work in no company the caller does not; callers cannot change their own access.")
            .ProducesValidationProblem()
            .RequirePermission(TenancyPermissions.AccessUpdate);
    }

    private static async Task<Results<Ok<ListPage<AccessRow>>, ProblemHttpResult>> List(
        TenancyDbContext db, IUserDirectory users, [AsParameters] ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await PageAsync(db, users, request, http, cancellationToken);
        return result.Problem is { } problem ? problem : TypedResults.Ok(result.ToPage(r => r));
    }

    /// <summary>One page of the access list exactly as the endpoint serves it (reports print it too).</summary>
    internal static async Task<ListResult<AccessRow>> PageAsync(TenancyDbContext db, IUserDirectory users, ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await users.QueryListAsync(AccessList.Key, request, http, cancellationToken);
        if (result.Problem is not null)
        {
            return result.Map(_ => (AccessRow)null!);
        }
        var ids = result.Rows.Select(u => u.Id).ToList();
        var access = await (from a in db.CompanyAccess.AsNoTracking()
                            where ids.Contains(a.UserId)
                            join c in db.Companies.AsNoTracking() on a.CompanyId equals c.Id
                            orderby c.Code
                            select new
                            {
                                a.UserId, a.CompanyId, c.Code, a.AllBranches,
                                Branches = a.AllBranches
                                    ? db.Branches.Count(b => b.CompanyId == a.CompanyId)
                                    : db.BranchAccess.Count(b => b.UserId == a.UserId && b.CompanyId == a.CompanyId),
                            })
            .ToListAsync(cancellationToken);
        var byUser = access.GroupBy(a => a.UserId).ToDictionary(g => g.Key,
            g => (IReadOnlyList<AccessCompanySummary>)g.Select(a => new AccessCompanySummary(a.CompanyId, a.Code, a.AllBranches, a.Branches)).ToList());
        return result.Map(u => new AccessRow(u.Id, u.DisplayName, u.Email, byUser.GetValueOrDefault(u.Id) ?? []));
    }

    private static async Task<Results<Ok<UserAccessDto>, ProblemHttpResult>> Get(
        Guid userId, TenancyDbContext db, IUserDirectory users, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var user = (await users.GetAsync([userId], cancellationToken)).GetValueOrDefault(userId);
        if (user is null)
        {
            return Problems.NotFound(http);
        }
        return TypedResults.Ok(await ReadAsync(db, users, caller, user, cancellationToken));
    }

    private static async Task<Results<Ok<UserAccessDto>, ProblemHttpResult>> Update(
        Guid userId, UpdateAccessRequest request, TenancyDbContext db, IUserDirectory users, ICurrentUser caller, HttpContext http,
        CancellationToken cancellationToken)
    {
        var validator = new Validator(http).Must(request.Companies is not null, "companies", "required");
        var wanted = request.Companies ?? [];
        validator.Must(wanted.All(c => c.CompanyId is not null), "companies", "tenancyAccessCompanyRequired")
            .Must(wanted.All(c => c.AllBranches is not null), "companies", "tenancyAccessAllBranchesRequired")
            .Must(wanted.Where(c => c.CompanyId is not null).Select(c => c.CompanyId).Distinct().Count() == wanted.Count(c => c.CompanyId is not null),
                "companies", "tenancyAccessCompanyTwice");
        // Only companies in the caller's own scope exist for the caller: nothing else can be granted.
        var companyIds = wanted.Where(c => c.CompanyId is not null).Select(c => c.CompanyId!.Value).Distinct().ToList();
        var visible = await db.Companies.AsNoTracking().Where(c => companyIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(cancellationToken);
        validator.Must(visible.Count == companyIds.Count, "companies", "unknownIds");
        var branchIds = wanted.Where(c => c.AllBranches == false).SelectMany(c => c.BranchIds ?? []).Distinct().ToList();
        var branches = await db.Branches.AsNoTracking().Where(b => branchIds.Contains(b.Id)).Select(b => new { b.Id, b.CompanyId }).ToListAsync(cancellationToken);
        foreach (var company in wanted.Where(c => c.CompanyId is not null && c.AllBranches == false))
        {
            var listed = (company.BranchIds ?? []).Distinct().ToList();
            validator.Must(listed.Count > 0, "companies", "tenancyAccessBranchesRequired")
                .Must(listed.All(id => branches.Any(b => b.Id == id && b.CompanyId == company.CompanyId)), "companies", "tenancyAccessBranchOfOtherCompany");
        }
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var user = (await users.GetAsync([userId], cancellationToken)).GetValueOrDefault(userId);
        if (user is null)
        {
            return Problems.NotFound(http);
        }
        // Access is a grant: never one's own, never on a stronger user, never beyond one's own.
        var before = await CompanyAccessRules.HoldingsAsync(db, userId, cancellationToken);
        if (await CompanyAccessRules.RefuseUserAsync(db, caller.UserId, caller.Has, userId, await users.GetPermissionsAsync(userId, cancellationToken),
                before.Count, cancellationToken) is { } refusal)
        {
            return Problems.Forbidden(http, refusal);
        }
        var mine = await CompanyAccessRules.HoldingsAsync(db, caller.UserId, cancellationToken);
        var after = wanted.ToDictionary(c => c.CompanyId!.Value,
            c => new CompanyHolding(c.CompanyId!.Value, c.AllBranches == true ? null : (c.BranchIds ?? []).ToHashSet()));
        if (!CompanyAccessRules.ChangeWithinCaller(mine, before, after))
        {
            return Problems.Forbidden(http, "tenancy.grantBeyondOwn");
        }

        var current = await db.CompanyAccess.Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        var currentBranches = await db.BranchAccess.Where(b => b.UserId == userId).ToListAsync(cancellationToken);
        foreach (var removed in current.Where(a => !companyIds.Contains(a.CompanyId)))
        {
            db.BranchAccess.RemoveRange(currentBranches.Where(b => b.CompanyId == removed.CompanyId));
            db.CompanyAccess.Remove(removed);
        }
        await db.SaveChangesAsync(cancellationToken);
        foreach (var company in wanted)
        {
            var companyId = company.CompanyId!.Value;
            var row = current.FirstOrDefault(a => a.CompanyId == companyId);
            if (row is null)
            {
                row = new UserCompanyAccess { UserId = userId, CompanyId = companyId };
                db.CompanyAccess.Add(row);
            }
            row.AllBranches = company.AllBranches!.Value;
            var keep = row.AllBranches ? [] : (company.BranchIds ?? []).Distinct().ToList();
            db.BranchAccess.RemoveRange(currentBranches.Where(b => b.CompanyId == companyId && !keep.Contains(b.BranchId)));
            db.BranchAccess.AddRange(keep.Where(id => currentBranches.All(b => b.BranchId != id))
                .Select(id => new UserBranchAccess { UserId = userId, CompanyId = companyId, BranchId = id }));
        }
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ReadAsync(db, users, caller, user, cancellationToken));
    }

    private static async Task<UserAccessDto> ReadAsync(TenancyDbContext db, IUserDirectory users, ICurrentUser caller, UserSummary user, CancellationToken cancellationToken)
    {
        var isCaller = user.Id == caller.UserId;
        var visible = await db.CompanyAccess.AsNoTracking().CountAsync(a => a.UserId == user.Id, cancellationToken);
        var readOnly = await CompanyAccessRules.RefuseUserAsync(db, caller.UserId, caller.Has, user.Id,
            await users.GetPermissionsAsync(user.Id, cancellationToken), visible, cancellationToken);
        var mine = await CompanyAccessRules.HoldingsAsync(db, caller.UserId, cancellationToken);
        var access = await (from a in db.CompanyAccess.AsNoTracking()
                            where a.UserId == user.Id
                            join c in db.Companies.AsNoTracking() on a.CompanyId equals c.Id
                            orderby c.Code
                            select new { a.CompanyId, a.AllBranches }).ToListAsync(cancellationToken);
        var branches = await (from b in db.BranchAccess.AsNoTracking()
                              where b.UserId == user.Id
                              join br in db.Branches.AsNoTracking() on b.BranchId equals br.Id
                              orderby br.Code
                              select new { b.CompanyId, b.BranchId }).ToListAsync(cancellationToken);
        var companies = await db.Companies.AsNoTracking().OrderBy(c => c.Code)
            .Select(c => new { c.Id, c.Code, c.LegalNameEn, c.LegalNameAr, c.IsActive }).ToListAsync(cancellationToken);
        var allBranches = await db.Branches.AsNoTracking().OrderBy(b => b.Code)
            .Select(b => new { b.Id, b.CompanyId, b.Code, b.NameEn, b.NameAr, b.IsActive }).ToListAsync(cancellationToken);
        return new UserAccessDto(user.Id, user.DisplayName, user.Email, isCaller,
            access.Select(a => new CompanyAccessDto(a.CompanyId, a.AllBranches,
                a.AllBranches ? [] : branches.Where(b => b.CompanyId == a.CompanyId).Select(b => b.BranchId).ToList())).ToList(),
            companies.Select(c => new AccessCompanyOption(c.Id, c.Code, c.LegalNameEn, c.LegalNameAr, c.IsActive,
                allBranches.Where(b => b.CompanyId == c.Id).Select(b => new AccessBranchOption(b.Id, b.Code, b.NameEn, b.NameAr, b.IsActive)).ToList(),
                mine.GetValueOrDefault(c.Id)?.AllBranches ?? true)).ToList(),
            readOnly is null, readOnly switch
            {
                null => null,
                "tenancy.cannotChangeOwnAccess" => "tenancy.access.readOnly.self",
                "tenancy.userBeyondOwn" => "tenancy.access.readOnly.permissions",
                _ => "tenancy.access.readOnly.companies",
            });
    }
}
