using System.Linq.Expressions;
using Erp.Kernel.Http;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Identity.Auth;
using Erp.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Users;

/// <summary>Activate or deactivate every user the users list's search and filter match (the list's
/// "all that match" selection).</summary>
/// <param name="Active">The state to set.</param>
/// <param name="Search">The list's search, exactly as the list was asked (empty: no search).</param>
/// <param name="Filter">The list's filter expression, exactly as the list was asked (empty: none).</param>
/// <param name="ExpectedCount">How many rows the list said match: the caller confirmed acting on that
/// many. When the users that match now differ in number, nothing changes and the answer is 409.</param>
public sealed record SetMatchingUsersActiveRequest(bool? Active, string? Search, string? Filter, int? ExpectedCount);

/// <param name="Matched">Users the search and filter match.</param>
/// <param name="Changed">Users whose state changed.</param>
/// <param name="Unchanged">Users already in that state.</param>
/// <param name="RefusedSelf">1 when the caller is among the users to deactivate (never done).</param>
/// <param name="RefusedBeyondOwn">Users holding a permission the caller does not hold (workspace-wide
/// or in one company), or any role in a company the caller does not work in (left alone, as one
/// user's edit would refuse them).</param>
public sealed record SetMatchingUsersActiveResponse(int Matched, int Changed, int Unchanged, int RefusedSelf, int RefusedBeyondOwn);

/// <summary>
/// Bulk change of the users list's matching rows in one set-based update. Every rule of a single
/// user's edit holds: never deactivate oneself, never change a user who holds a permission the
/// caller does not hold, in the same company or everywhere, or who holds roles in a company the
/// caller does not work in (<see cref="BeyondCallerAsync"/>). Each changed row is captured in the audit trail by the database trigger,
/// with the caller as the actor, like any other update. Row-level security and the tenant filter
/// confine both the match and the update to the caller's workspace.
/// </summary>
internal static class UserBulkEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/users/matching/active", SetMatchingActive)
            .WithName("identity.users.setMatchingActive")
            .WithSummary("Activate or deactivate every user the users list's search and filter match (the list's \"all that match\" selection). expectedCount must equal the number that match now, or nothing changes (409). The caller is never deactivated, and users holding a permission the caller lacks (workspace-wide or in one company) or any role in a company the caller does not work in are left alone; the answer counts each.")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.UsersUpdate);
    }

    private static async Task<Results<Ok<SetMatchingUsersActiveResponse>, ProblemHttpResult>> SetMatchingActive(
        SetMatchingUsersActiveRequest request, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, TimeProvider time,
        HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http)
            .Required("active", request.Active)
            .Required("expectedCount", request.ExpectedCount)
            .Must(request.ExpectedCount is null or >= 0, "expectedCount", "list.matchingCountNegative");
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        IQueryable<User> matching;
        try
        {
            matching = catalog.ListBinding<User>(UsersList.Key)
                .Matching(db.Users, new ListRequest { Search = request.Search, Filter = request.Filter });
        }
        catch (ListQueryException e)
        {
            return ListBinding<User>.ToProblem(http, e);
        }
        var matched = await matching.CountAsync(cancellationToken);
        if (matched != request.ExpectedCount)
        {
            return Problems.Result(http, StatusCodes.Status409Conflict, "list.matchingChanged", null, request.ExpectedCount, matched);
        }
        var active = request.Active!.Value;
        var beyond = await BeyondCallerAsync(db, caller, catalog, cancellationToken);
        var toChange = matching.Where(u => u.IsActive != active);
        var unchanged = matched - await toChange.CountAsync(cancellationToken);
        var refusedSelf = !active && await toChange.AnyAsync(u => u.Id == caller.UserId, cancellationToken) ? 1 : 0;
        var allowed = toChange.Where(u => active || u.Id != caller.UserId);
        var refusedBeyond = await allowed.Where(beyond).CountAsync(cancellationToken);
        var callerId = caller.UserId;
        var now = time.GetUtcNow();
        var within = Expression.Lambda<Func<User, bool>>(Expression.Not(beyond.Body), beyond.Parameters);
        var changed = await allowed
            .Where(within)
            .ExecuteUpdateAsync(set => set
                .SetProperty(u => u.IsActive, active)
                .SetProperty(u => u.UpdatedAt, now)
                .SetProperty(u => u.UpdatedBy, callerId), cancellationToken);
        return TypedResults.Ok(new SetMatchingUsersActiveResponse(matched, changed, unchanged, refusedSelf, refusedBeyond));
    }

    /// <summary>
    /// The users the caller may not act on, as one set-based condition: the same rule
    /// <see cref="UserAccess.RefusalAsync"/> applies to one user (critic p03 round 5, finding R1:
    /// judging only workspace-wide roles let a helpdesk clerk deactivate a company manager whose
    /// roles are all held in one company). A user is beyond the caller when they hold
    /// <list type="bullet">
    /// <item>a workspace-wide role granting a permission the caller does not hold everywhere;</item>
    /// <item>a role in one company granting a permission the caller holds neither everywhere nor
    /// in that company;</item>
    /// <item>any role in a company the caller does not work in (row-level security and the
    /// company filter hide those rows, so the user's count of company roles is larger than the
    /// rows the caller sees): what it grants cannot be judged from here.</item>
    /// </list>
    /// Only permissions of the catalogue count, as for one user.
    /// </summary>
    internal static async Task<Expression<Func<User, bool>>> BeyondCallerAsync(IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog,
        CancellationToken cancellationToken)
    {
        var callerGrants = await GrantQueries.ForCallerAsync(db, caller, catalog, cancellationToken);
        var roles = await db.Roles.AsNoTracking().Select(r => new { r.Id, r.Permissions }).ToListAsync(cancellationToken);
        var granted = roles.ToDictionary(r => r.Id, r => r.Permissions.Where(catalog.IsPermission).ToList());
        // Roles granting something the caller does not hold everywhere.
        var strong = granted.Where(r => !callerGrants.CoversAll(r.Value, null)).Select(r => r.Key).ToList();
        // Of those, the ones the caller's own roles in a company cover there ("role/company").
        var coveredThere = callerGrants.ByCompany.Keys
            .SelectMany(company => strong.Where(role => callerGrants.CoversAll(granted[role], company)).Select(role => $"{role}/{company}"))
            .ToList();
        return u =>
            db.UserRoles.Any(ur => ur.UserId == u.Id && strong.Contains(ur.RoleId)) ||
            db.UserCompanyRoles.Any(ucr => ucr.UserId == u.Id && strong.Contains(ucr.RoleId) &&
                                           !coveredThere.Contains(ucr.RoleId.ToString() + "/" + ucr.CompanyId.ToString())) ||
            u.CompanyRoleCount > db.UserCompanyRoles.Count(ucr => ucr.UserId == u.Id);
    }
}
