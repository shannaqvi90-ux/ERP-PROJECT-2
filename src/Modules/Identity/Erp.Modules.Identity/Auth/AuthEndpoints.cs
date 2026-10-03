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

public sealed record SignInRequest(string? Email, string? Password, string? Workspace, bool? IssueToken);

/// <summary>The signed-in user as the shell shows them. <c>Numerals</c> is latn or arab: the digits
/// Arabic screens use.</summary>
public sealed record SessionUser(Guid Id, string Email, string DisplayName, string Language, string Numerals);

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
            .WithSummary("Sign in with e-mail and password. Sets the session cookie; returns a bearer token too when issueToken is true.")
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
            .MaxLength("workspace", request.Workspace, 40);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }

        var outcome = await signIn.SignInAsync(request.Email!.Trim(), request.Password!, request.Workspace, http, cancellationToken);
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
        return TypedResults.NoContent();
    }

    private static async Task<Ok<SessionResponse>> GetSession(HttpContext http, SessionPayload payload, CancellationToken cancellationToken)
    {
        if (http.User.Identity?.IsAuthenticated != true || http.User.FindUserId() is not { } userId)
        {
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
            .Select(u => new SessionUser(u.Id, u.Email, u.DisplayName, u.Language, u.Numerals))
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
