using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Erp.Kernel.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Erp.Kernel.Security;

public static class SessionAuthenticationDefaults
{
    public const string Scheme = "ErpSession";
    public const string CookieName = "erp_session";

    /// <summary>Header that unsafe cookie-authenticated requests must carry (CSRF defence).</summary>
    public const string RequestHeader = "X-Erp-Request";
}

public static class ErpClaims
{
    public const string TenantId = "erp:tenant";
    public const string UserId = "erp:user";
    public const string SessionId = "erp:session";
    public const string Permission = "erp:permission";
    public const string Language = "erp:language";
    public const string Email = "erp:email";
    public const string DisplayName = "erp:name";
    public const string ExpiresAt = "erp:expires";
}

/// <summary>A valid, unexpired, unrevoked session and what its user may do.</summary>
public sealed record ResolvedSession(
    Guid SessionId,
    Guid TenantId,
    Guid UserId,
    string Email,
    string DisplayName,
    string Language,
    DateTimeOffset ExpiresAt,
    IReadOnlyCollection<string> Permissions);

/// <summary>
/// Turns a session token into a tenant-bound principal. Implemented by the identity module. The
/// implementation must bind the scope's <see cref="Data.ErpDbSession"/> to the session's tenant
/// (and to nothing else): the tenant comes only from the session, never from anything the client
/// sends.
/// </summary>
public interface ISessionResolver
{
    Task<ResolvedSession?> ResolveAsync(byte[] tokenHash, CancellationToken cancellationToken);
}

/// <summary>Opaque session tokens: 256 random bits, only the SHA-256 hash is stored.</summary>
public static class SessionTokens
{
    public static string Generate() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token));

    /// <summary>Tokens are 43 base64url characters; anything else is rejected before any lookup.</summary>
    public static bool LooksValid(string? token) =>
        token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class SessionAuthenticationOptions : AuthenticationSchemeOptions;

/// <summary>Marks the request while the authentication handler resolves its session token. The
/// tenant-isolation gate uses it to prove that the reviewed cross-tenant session lookup runs only
/// there and never from an endpoint.</summary>
public static class SessionResolution
{
    public const string InProgressItem = "erp.session.resolving";

    public static bool IsInProgress(HttpContext? context) =>
        context?.Items.TryGetValue(InProgressItem, out var value) == true && value is true;
}

/// <summary>Reads the session token from the <c>erp_session</c> cookie or an
/// <c>Authorization: Bearer</c> header (API clients), and resolves it.</summary>
internal sealed class SessionAuthenticationHandler(
    IOptionsMonitor<SessionAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<SessionAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Path.StartsWithSegments("/api"))
        {
            return AuthenticateResult.NoResult();
        }
        var token = ReadToken(Request);
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }
        if (!SessionTokens.LooksValid(token))
        {
            return AuthenticateResult.Fail("Malformed session token.");
        }
        var resolver = Context.RequestServices.GetRequiredService<ISessionResolver>();
        ResolvedSession? session;
        // Marks the one place the reviewed session lookup may run (the isolation gate traces it).
        Context.Items[SessionResolution.InProgressItem] = true;
        try
        {
            session = await resolver.ResolveAsync(SessionTokens.Hash(token), Context.RequestAborted);
        }
        finally
        {
            Context.Items.Remove(SessionResolution.InProgressItem);
        }
        if (session is null)
        {
            return AuthenticateResult.Fail("Session expired, revoked or unknown.");
        }

        var claims = new List<Claim>
        {
            new(ErpClaims.TenantId, session.TenantId.ToString()),
            new(ErpClaims.UserId, session.UserId.ToString()),
            new(ErpClaims.SessionId, session.SessionId.ToString()),
            new(ErpClaims.Language, session.Language),
            new(ErpClaims.Email, session.Email),
            new(ErpClaims.DisplayName, session.DisplayName),
            new(ErpClaims.ExpiresAt, session.ExpiresAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.NameIdentifier, session.UserId.ToString()),
        };
        claims.AddRange(session.Permissions.Select(p => new Claim(ErpClaims.Permission, p)));
        var identity = new ClaimsIdentity(claims, Scheme.Name, ErpClaims.Email, ClaimTypes.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    /// <summary>Bearer header wins over the cookie.</summary>
    internal static string? ReadToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }
        return request.Cookies.TryGetValue(SessionAuthenticationDefaults.CookieName, out var cookie) ? cookie : null;
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        await Problems.Write(Context, StatusCodes.Status401Unauthorized, "auth.required");
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        await Problems.Write(Context, StatusCodes.Status403Forbidden, "auth.forbidden");
    }
}

public static class PrincipalExtensions
{
    public static bool HasPermission(this ClaimsPrincipal principal, string permission) =>
        principal.HasClaim(ErpClaims.Permission, permission);

    public static Guid? FindTenantId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ErpClaims.TenantId), out var id) ? id : null;

    public static Guid? FindUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ErpClaims.UserId), out var id) ? id : null;

    public static Guid? FindSessionId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ErpClaims.SessionId), out var id) ? id : null;

    public static IReadOnlyList<string> Permissions(this ClaimsPrincipal principal) =>
        principal.FindAll(ErpClaims.Permission).Select(c => c.Value).Order(StringComparer.Ordinal).ToList();
}

/// <summary>The signed-in user of the current request.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid TenantId { get; }
    Guid UserId { get; }
    Guid SessionId { get; }
    string Language { get; }
    bool Has(string permission);
    IReadOnlyList<string> Permissions { get; }
}

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal Principal => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;
    public Guid TenantId => Principal.FindTenantId() ?? throw new InvalidOperationException("No signed-in user.");
    public Guid UserId => Principal.FindUserId() ?? throw new InvalidOperationException("No signed-in user.");
    public Guid SessionId => Principal.FindSessionId() ?? throw new InvalidOperationException("No signed-in user.");
    public string Language => Principal.FindFirstValue(ErpClaims.Language) ?? "en";
    public bool Has(string permission) => Principal.HasPermission(permission);
    public IReadOnlyList<string> Permissions => Principal.Permissions();
}

/// <summary>Unsafe requests (POST, PUT, PATCH, DELETE) to the API must carry either the
/// <c>X-Erp-Request</c> header or an <c>Authorization</c> header. Browsers cannot add either to a
/// cross-site request without a CORS preflight, which the API never grants, so a forged form post
/// or image request cannot act with the user's cookie.</summary>
internal sealed class CsrfMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(request.Method) &&
            !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method) &&
            !request.Headers.ContainsKey(SessionAuthenticationDefaults.RequestHeader) &&
            !request.Headers.ContainsKey("Authorization"))
        {
            await Problems.Write(context, StatusCodes.Status400BadRequest, "request.missingRequestHeader");
            return;
        }
        await next(context);
    }
}
