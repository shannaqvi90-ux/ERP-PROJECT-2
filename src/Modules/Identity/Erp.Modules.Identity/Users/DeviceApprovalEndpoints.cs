using System.ComponentModel.DataAnnotations;
using Erp.Kernel.Hosting;
using Erp.Kernel.Http;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Identity.Auth;
using Erp.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Validator = Erp.Kernel.Http.Validator;

namespace Erp.Modules.Identity.Users;

/// <summary>The device code a paused sign-in screen showed: eight characters, upper or lower case,
/// with or without the hyphen and spaces.</summary>
public sealed record ApproveDeviceRequest(
    [property: ApiExample("K7QM-3XRA"), RegularExpression(DeviceApprovalEndpoints.CodePattern)] string? Code);

/// <summary>Until when the device may make its first sign-in (later, the code no longer lets it in).</summary>
public sealed record DeviceApprovalResponse(DateTimeOffset Until);

/// <summary>
/// Letting one device sign in from an address many clients share while sign-in from it is paused
/// (<see cref="DeviceApprovals"/>): the user, signed in on another device and recently, or an
/// administrator whose access covers the user's, enters the code that device's sign-in screen
/// showed. One approval at a time per account (a new one replaces the last); the device's first
/// sign-in uses it up. It grants nothing but that: the device still needs the right password, and
/// its own failures pause it like any client's.
/// </summary>
internal static class DeviceApprovalEndpoints
{
    /// <summary>Eight symbols of the code alphabet (either case), hyphens and spaces anywhere.</summary>
    public const string CodePattern = "^[ -]*(?:[ABCDEFGHJKMNPQRSTUVWXYZ23456789abcdefghjkmnpqrstuvwxyz][ -]*){8}$";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/users/{id:guid}/devices/approve", ApproveForUser)
            .WithName("identity.users.approveDevice")
            .WithSummary("Let one device of another user sign in while sign-in from its shared network address (a gateway or proxy) is paused: enter the device code its sign-in screen showed. For DeviceApprovalMinutes (60 by default) that browser, and no other, is a client of its own on this account: its right password signs in, and its first sign-in uses the approval up. A new approval replaces the last. Only users whose access is within the caller's own, never oneself (use POST /api/identity/me/devices/approve).")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.UsersUpdate);

        group.MapPost("/me/devices/approve", ApproveMine)
            .WithName("identity.me.approveDevice")
            .WithSummary("Let one of your own devices sign in while sign-in from its shared network address is paused: enter the device code its sign-in screen showed. Needs a session signed in within the last few minutes (403 auth.recentSignInRequiredDevice otherwise), so a browser left signed in cannot let other devices try the password.")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.ProfileUpdate);
    }

    private static async Task<Results<Ok<DeviceApprovalResponse>, ProblemHttpResult>> ApproveForUser(
        Guid id, ApproveDeviceRequest request, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, IOptions<AuthOptions> options, TimeProvider time,
        HttpContext http, CancellationToken cancellationToken)
    {
        if (await UserEndpoints.TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)
        {
            return problem;
        }
        if (Invalid(request, http) is { } invalid)
        {
            return invalid;
        }
        return await ApproveAsync(db, id, request, options.Value, time, http, cancellationToken);
    }

    private static async Task<Results<Ok<DeviceApprovalResponse>, ProblemHttpResult>> ApproveMine(
        ApproveDeviceRequest request, IdentityDbContext db, ICurrentUser caller, IOptions<AuthOptions> options, TimeProvider time, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (Invalid(request, http) is { } invalid)
        {
            return invalid;
        }
        var signedInAt = await db.Sessions.AsNoTracking().Where(s => s.Id == caller.SessionId).Select(s => (DateTimeOffset?)s.CreatedAt).SingleOrDefaultAsync(cancellationToken);
        if (signedInAt is null || signedInAt.Value.AddMinutes(Math.Clamp(options.Value.PasskeyRecentSignInMinutes, 1, 60)) <= time.GetUtcNow())
        {
            return Problems.Forbidden(http, "auth.recentSignInRequiredDevice");
        }
        return await ApproveAsync(db, caller.UserId, request, options.Value, time, http, cancellationToken);
    }

    private static ProblemHttpResult? Invalid(ApproveDeviceRequest request, HttpContext http)
    {
        var validator = new Validator(http).Required("code", request.Code);
        if (validator.IsValid && DeviceApprovals.Normalize(request.Code) is null)
        {
            validator.Add("code", "identityDeviceCodeInvalid");
        }
        return validator.IsValid ? null : validator.ToResult();
    }

    private static async Task<Results<Ok<DeviceApprovalResponse>, ProblemHttpResult>> ApproveAsync(
        IdentityDbContext db, Guid userId, ApproveDeviceRequest request, AuthOptions options, TimeProvider time, HttpContext http, CancellationToken cancellationToken)
    {
        // A concurrent request may have removed the user since the target check.
        if (await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken) is not { } user)
        {
            return Problems.NotFound(http);
        }
        var until = time.GetUtcNow() + DeviceApprovals.Lifetime(options);
        user.DeviceApprovalHash = DeviceApprovals.Hash(request.Code);
        user.DeviceApprovalUntil = until;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new DeviceApprovalResponse(until));
    }
}
