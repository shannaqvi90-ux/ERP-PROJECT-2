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

/// <summary>The signed-in user's own interface preferences. Fields left out (null) keep their
/// current value; at least one must be given.</summary>
/// <param name="Language">Interface language: en or ar.</param>
/// <param name="Numerals">Digits on Arabic screens: latn (0123) or arab (٠١٢٣).</param>
public sealed record UpdatePreferencesRequest(
    [property: AllowedTextValues(Languages.English, Languages.Arabic)] string? Language,
    [property: AllowedTextValues(NumeralSystems.Latin, NumeralSystems.ArabicIndic)] string? Numerals);

internal static class ProfileEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/me/preferences", UpdatePreferences)
            .WithName("identity.me.preferences")
            .WithSummary("Change the signed-in user's own preferences (interface language, digits on Arabic screens).")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.ProfileUpdate);
    }

    private static async Task<Results<Ok<SessionUser>, ProblemHttpResult>> UpdatePreferences(
        UpdatePreferencesRequest request, IdentityDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http)
            .Must(request.Language is not null || request.Numerals is not null, "language", "nothingToChange")
            .OneOf("language", request.Language, Languages.All)
            .OneOf("numerals", request.Numerals, NumeralSystems.All);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == caller.UserId, cancellationToken);
        if (user is null)
        {
            return Problems.NotFound(http);
        }
        user.Language = request.Language ?? user.Language;
        user.Numerals = request.Numerals ?? user.Numerals;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new SessionUser(user.Id, user.Email, user.DisplayName, user.Language, user.Numerals));
    }
}
