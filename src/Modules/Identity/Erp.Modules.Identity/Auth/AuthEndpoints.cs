using System.Text.Json.Serialization;
using Erp.Kernel.Data;
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
using Microsoft.Extensions.DependencyInjection;
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
                var response = await payload.BuildForNewSessionAsync(success.TenantId, success.UserId, success.SessionId, success.ExpiresAt, cancellationToken);
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
        return TypedResults.Ok(await payload.BuildAsync(userId, expires, http.User.Permissions(), cancellationToken));
    }
}

/// <summary>Builds the session payload for a user of the tenant the unit of work is bound to.</summary>
internal sealed class SessionPayload(IdentityDbContext db, ITenantDirectory tenants, ShellMenu menu, IServiceProvider services)
{
    /// <summary>The payload of the request's own session: its permissions are the ones every
    /// endpoint of this request checks (the working company's included).</summary>
    public Task<SessionResponse> BuildAsync(Guid userId, DateTimeOffset? expiresAt, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken) =>
        BuildCoreAsync(userId, expiresAt, permissions, cancellationToken);

    /// <summary>
    /// The payload of a session just signed in to: the same steps the next request's
    /// authentication takes (the session's scope bound, then the permissions of the working
    /// company), so the first screen shows exactly what the user may do there. They run in a
    /// fresh service scope with its own unit of work bound to the new session's tenant and user:
    /// a request that arrived with another valid session has already bound that session's
    /// company and branch scope (set once per request), which must neither be widened nor
    /// reused for the new session.
    /// </summary>
    public async Task<SessionResponse> BuildForNewSessionAsync(Guid tenantId, Guid userId, Guid sessionId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await using var fresh = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var provider = fresh.ServiceProvider;
        var unit = provider.GetRequiredService<ErpDbSession>();
        unit.CorrelationId = services.GetRequiredService<ErpDbSession>().CorrelationId;
        // Reads only; the scope's transaction is rolled back when it is disposed.
        await unit.BeginAsync(tenantId, userId, ErpDbSession.UserActorKind, cancellationToken);
        var grants = provider.GetRequiredService<SessionGrants>();
        var held = await grants.LoadAsync(userId, cancellationToken);
        var freshDb = provider.GetRequiredService<IdentityDbContext>();
        var user = await freshDb.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Email, u.DisplayName, u.Language }).SingleAsync(cancellationToken);
        var resolved = new ResolvedSession(sessionId, tenantId, userId, user.Email, user.DisplayName, user.Language, expiresAt, held.Everywhere.ToList());
        foreach (var binder in provider.GetServices<ISessionScopeBinder>())
        {
            await binder.BindAsync(resolved, cancellationToken);
        }
        IReadOnlyCollection<string> permissions = resolved.Permissions;
        foreach (var scope in provider.GetServices<ISessionPermissionScope>())
        {
            permissions = await scope.ScopeAsync(resolved, permissions, cancellationToken);
        }
        return await provider.GetRequiredService<SessionPayload>().BuildCoreAsync(userId, expiresAt, permissions, cancellationToken);
    }

    private async Task<SessionResponse> BuildCoreAsync(Guid userId, DateTimeOffset? expiresAt, IReadOnlyCollection<string> held, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new SessionUser(u.Id, u.Email, u.DisplayName, u.Language, u.Numerals, u.DisplayNameAr))
            .SingleAsync(cancellationToken);
        var tenant = await tenants.GetCurrentAsync(cancellationToken)
                     ?? throw new InvalidOperationException("The session's tenant is not active.");
        var permissions = held.ToHashSet(StringComparer.Ordinal);
        return new SessionResponse(
            true,
            user,
            new SessionTenant(tenant.Id, tenant.Code, tenant.NameEn, tenant.NameAr),
            permissions.Order(StringComparer.Ordinal).ToList(),
            menu.For(permissions).Select(m => new SessionMenuItem(m.Key, m.LabelKey, m.Path, m.Group)).ToList(),
            expiresAt);
    }
}
