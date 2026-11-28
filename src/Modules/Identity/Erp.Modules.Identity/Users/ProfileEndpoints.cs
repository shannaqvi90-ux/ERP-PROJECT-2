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

/// <summary>The signed-in user's own look. Fields left out (null) keep their current value; at least one must be given.</summary>
/// <param name="Theme">system (follow the device), light, dark or hc (high contrast).</param>
/// <param name="Density">compact or comfortable rows and fields.</param>
public sealed record UpdateLookRequest(
    [property: AllowedTextValues(LookChoices.System, LookChoices.Light, LookChoices.Dark, LookChoices.HighContrast)] string? Theme,
    [property: AllowedTextValues(LookChoices.Compact, LookChoices.Comfortable)] string? Density);

/// <summary>The values of the look a user chooses (kept in step with web/src/kernel/theme.ts).</summary>
public static class LookChoices
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";
    public const string HighContrast = "hc";
    public const string Compact = "compact";
    public const string Comfortable = "comfortable";

    public static readonly IReadOnlyList<string> Themes = [System, Light, Dark, HighContrast];
    public static readonly IReadOnlyList<string> Densities = [Compact, Comfortable];
}

internal static class ProfileEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/me/preferences", UpdatePreferences)
            .WithName("identity.me.preferences")
            .WithSummary("Change the signed-in user's own preferences (interface language, digits on Arabic screens).")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.ProfileUpdate);
        group.MapPut("/me/look", UpdateLook)
            .WithName("identity.me.look")
            .WithSummary("Change the signed-in user's own look (theme and density of the screens).")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.ProfileUpdate);
    }

    private static async Task<Results<Ok<SessionUser>, ProblemHttpResult>> UpdateLook(
        UpdateLookRequest request, IdentityDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http)
            .Must(request.Theme is not null || request.Density is not null, "theme", "nothingToChange")
            .OneOf("theme", request.Theme, LookChoices.Themes)
            .OneOf("density", request.Density, LookChoices.Densities);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == caller.UserId, cancellationToken);
        if (user is null)
        {
            return Problems.NotFound(http);
        }
        user.Theme = request.Theme ?? user.Theme;
        user.Density = request.Density ?? user.Density;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new SessionUser(user.Id, user.Email, user.DisplayName, user.Language, user.Numerals, user.DisplayNameAr, user.Theme, user.Density));
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
        return TypedResults.Ok(new SessionUser(user.Id, user.Email, user.DisplayName, user.Language, user.Numerals, user.DisplayNameAr, user.Theme, user.Density));
    }
}
