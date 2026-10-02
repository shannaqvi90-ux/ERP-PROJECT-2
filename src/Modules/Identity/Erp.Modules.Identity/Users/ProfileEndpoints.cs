using Erp.Kernel.Http;
using Erp.Kernel.Localization;
using Erp.Kernel.Security;
using Erp.Modules.Identity.Auth;
using Erp.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Users;

public sealed record UpdatePreferencesRequest(string? Language);

internal static class ProfileEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/me/preferences", UpdatePreferences)
            .WithName("identity.me.preferences")
            .WithSummary("Change the signed-in user's own preferences (interface language).")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.ProfileUpdate);
    }

    private static async Task<Results<Ok<SessionUser>, ProblemHttpResult>> UpdatePreferences(
        UpdatePreferencesRequest request, IdentityDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http)
            .Required("language", request.Language).OneOf("language", request.Language, Languages.All);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == caller.UserId, cancellationToken);
        if (user is null)
        {
            return Problems.NotFound(http);
        }
        user.Language = request.Language!;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new SessionUser(user.Id, user.Email, user.DisplayName, user.Language));
    }
}
