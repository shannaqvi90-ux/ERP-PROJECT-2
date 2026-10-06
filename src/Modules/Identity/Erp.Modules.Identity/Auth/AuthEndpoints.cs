using System.Text.Json.Serialization;
using Erp.Kernel.Hosting;
using Erp.Kernel.Http;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Erp.Modules.Identity.Auth;

/// <summary>Sign-in. <c>NewPassword</c> changes the password as part of signing in: required when
/// the password is a one-time set-up code, and how a signed-in user changes their own password
/// (proving the current one).</summary>
public sealed record SignInRequest(string? Email, string? Password, string? Workspace, bool? IssueToken, string? NewPassword = null);

/// <summary>The signed-in user as the shell shows them. <c>Numerals</c> is latn or arab: the digits
/// Arabic screens use. <c>DisplayNameAr</c>: the name in Arabic script, shown on Arabic screens
/// (null when the user has none; the shell then shows <c>DisplayName</c>).</summary>
public sealed record SessionUser(Guid Id, string Email, string DisplayName, string Language, string Numerals, string? DisplayNameAr = null);

public sealed record SessionTenant(Guid Id, string Code, string NameEn, string NameAr);

public sealed record SessionMenuItem(string Key, string LabelKey, string Path, string? Group);

/// <summary>Who is signed in, to which workspace, what they may do and what the shell shows.</summary>
public sealed record SessionResponse(
    bool Authenticated,
    SessionUser? User,
    SessionTenant? Tenant,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<SessionMenuItem> Menu,
    DateTimeOffset? ExpiresAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Token = null)
{
    public static readonly SessionResponse Anonymous = new(false, null, null, [], [], null);
}

internal static class AuthEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/sign-in", SignIn)
            .WithName("auth.signIn")
            .WithSummary("Sign in with e-mail and password (or a one-time set-up code). Sets the session cookie; returns a bearer token too when issueToken is true. With newPassword it also changes the password; a set-up code answers 409 auth.passwordChangeRequired until one is given.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(ErpPlatform.SignInRateLimit)
            .AllowAnonymousReviewed("Signing in is how a session starts.");

        group.MapPost("/sign-out", SignOut)
            .WithName("auth.signOut")
            .WithSummary("Revoke the current session and clear the cookie.")
            .AllowAnonymousReviewed("Signing out must work even when the session has already expired; it only ever revokes the caller's own session.");

        group.MapGet("/session", GetSession)
            .WithName("auth.session")
            .WithSummary("The caller's session: user, workspace, permissions and menu, or authenticated = false.")
            .AllowAnonymousReviewed("The sign-in screen asks whether a session exists; returns only the caller's own session.");
    }

    private static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> SignIn(
        SignInRequest request,
        SignInService signIn,
        SessionPayload payload,
        IOptions<AuthOptions> options,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var validator = new Validator(http)
            .Required("email", request.Email).Email("email", request.Email?.Trim())
            .Required("password", request.Password).MaxLength("password", request.Password, 1024)
            .MaxLength("workspace", request.Workspace, 40)
            .MaxLength("newPassword", request.NewPassword, 1024);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }

        var outcome = await signIn.SignInAsync(request.Email!.Trim(), request.Password!, string.IsNullOrEmpty(request.NewPassword) ? null : request.NewPassword,
            request.Workspace, http, cancellationToken);
        switch (outcome)
        {
            case SignInOutcome.Succeeded success:
                http.Response.Cookies.Append(Kernel.Security.SessionAuthenticationDefaults.CookieName, success.Token, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = options.Value.AlwaysSecureCookie || http.Request.IsHttps,
                    Path = "/",
                    Expires = success.ExpiresAt,
                    IsEssential = true,
                });
                var response = await payload.BuildAsync(success.UserId, success.ExpiresAt, cancellationToken);
                return TypedResults.Ok(request.IssueToken == true ? response with { Token = success.Token } : response);
            case SignInOutcome.PasswordChangeRequired:
                return Problems.Result(http, StatusCodes.Status409Conflict, "auth.passwordChangeRequired");
            case SignInOutcome.NewPasswordInvalid invalid:
                return new Validator(http).Add("newPassword", invalid.Code, invalid.Args).ToResult();
            case SignInOutcome.ChooseWorkspace choose:
                var problem = Problems.Create(http, StatusCodes.Status409Conflict, "auth.chooseWorkspace");
                problem.Extensions["workspaces"] = choose.Workspaces;
                return TypedResults.Problem(problem);
            default:
                return Problems.Result(http, StatusCodes.Status401Unauthorized, "auth.signInFailed");
        }
    }

    private static async Task<NoContent> SignOut(HttpContext http, IdentityDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        if (http.User.FindSessionId() is { } sessionId)
        {
            var now = time.GetUtcNow();
            await db.Sessions.Where(s => s.Id == sessionId && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
        }
        http.Response.Cookies.Delete(Kernel.Security.SessionAuthenticationDefaults.CookieName, new CookieOptions { Path = "/", HttpOnly = true, SameSite = SameSiteMode.Strict });
        // Signing out ends the person's use of this browser: every cookie of the site goes, also
        // one a screen might have scoped to a path the signing-out document cannot see (the shell
        // forgets the ones it can see itself, kernel/deviceState). Storage is left to the shell,
        // which keeps the device's own settings (language, digits).
        http.Response.Headers["Clear-Site-Data"] = "\"cookies\"";
        return TypedResults.NoContent();
    }

    private static async Task<Ok<SessionResponse>> GetSession(HttpContext http, SessionPayload payload, CancellationToken cancellationToken)
    {
        if (http.User.Identity?.IsAuthenticated != true || http.User.FindUserId() is not { } userId)
        {
            // Always 200, also for an expired, revoked, unknown or malformed session cookie: a
            // fresh visit logs no failed request. The cookie is left alone; the next sign-in
            // replaces it.
            return TypedResults.Ok(SessionResponse.Anonymous);
        }
        var expires = long.TryParse(http.User.FindFirst(ErpClaims.ExpiresAt)?.Value, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : (DateTimeOffset?)null;
        return TypedResults.Ok(await payload.BuildAsync(userId, expires, cancellationToken));
    }
}

/// <summary>Builds the session payload for a user of the tenant the unit of work is bound to.</summary>
internal sealed class SessionPayload(IdentityDbContext db, ITenantDirectory tenants, ShellMenu menu, ModuleCatalog catalog)
{
    public async Task<SessionResponse> BuildAsync(Guid userId, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new SessionUser(u.Id, u.Email, u.DisplayName, u.Language, u.Numerals, u.DisplayNameAr))
            .SingleAsync(cancellationToken);
        var tenant = await tenants.GetCurrentAsync(cancellationToken)
                     ?? throw new InvalidOperationException("The session's tenant is not active.");
        var permissions = await PermissionQueries.ForUserAsync(db, userId, catalog, cancellationToken);
        return new SessionResponse(
            true,
            user,
            new SessionTenant(tenant.Id, tenant.Code, tenant.NameEn, tenant.NameAr),
            permissions.Order(StringComparer.Ordinal).ToList(),
            menu.For(permissions).Select(m => new SessionMenuItem(m.Key, m.LabelKey, m.Path, m.Group)).ToList(),
            expiresAt);
    }
}
