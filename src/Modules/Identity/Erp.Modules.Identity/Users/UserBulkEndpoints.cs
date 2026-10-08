using Erp.Kernel.Http;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
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
/// <param name="RefusedBeyondOwn">Users holding a permission the caller does not hold (left alone,
/// as one user's edit would refuse them).</param>
public sealed record SetMatchingUsersActiveResponse(int Matched, int Changed, int Unchanged, int RefusedSelf, int RefusedBeyondOwn);

/// <summary>
/// Bulk change of the users list's matching rows in one set-based update. Every rule of a single
/// user's edit holds: never deactivate oneself, never change a user who holds a permission the
/// caller does not hold. Each changed row is captured in the audit trail by the database trigger,
/// with the caller as the actor, like any other update. Row-level security and the tenant filter
/// confine both the match and the update to the caller's workspace.
/// </summary>
internal static class UserBulkEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/users/matching/active", SetMatchingActive)
            .WithName("identity.users.setMatchingActive")
            .WithSummary("Activate or deactivate every user the users list's search and filter match (the list's \"all that match\" selection). expectedCount must equal the number that match now, or nothing changes (409). The caller is never deactivated and users holding a permission the caller lacks are left alone; the answer counts each.")
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
        // Roles granting a permission (of the catalogue) the caller does not hold: their holders are
        // beyond the caller, as UserAccess.WithinCallerAsync judges one user.
        var roles = await db.Roles.AsNoTracking().Select(r => new { r.Id, r.Permissions }).ToListAsync(cancellationToken);
        var strongRoles = roles.Where(r => r.Permissions.Where(catalog.IsPermission).Any(p => !caller.Has(p))).Select(r => r.Id).ToList();
        var toChange = matching.Where(u => u.IsActive != active);
        var unchanged = matched - await toChange.CountAsync(cancellationToken);
        var refusedSelf = !active && await toChange.AnyAsync(u => u.Id == caller.UserId, cancellationToken) ? 1 : 0;
        var allowed = toChange.Where(u => active || u.Id != caller.UserId);
        var beyond = allowed.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && strongRoles.Contains(ur.RoleId)));
        var refusedBeyond = strongRoles.Count == 0 ? 0 : await beyond.CountAsync(cancellationToken);
        var callerId = caller.UserId;
        var now = time.GetUtcNow();
        var changed = await allowed
            .Where(u => !db.UserRoles.Any(ur => ur.UserId == u.Id && strongRoles.Contains(ur.RoleId)))
            .ExecuteUpdateAsync(set => set
                .SetProperty(u => u.IsActive, active)
                .SetProperty(u => u.UpdatedAt, now)
                .SetProperty(u => u.UpdatedBy, callerId), cancellationToken);
        return TypedResults.Ok(new SetMatchingUsersActiveResponse(matched, changed, unchanged, refusedSelf, refusedBeyond));
    }
}
