using Erp.Kernel.Http;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Identity.Contracts;
using Erp.Modules.Identity.Users;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Erp.Modules.Identity.Auth.Passkeys;

/// <summary>One of the user's passkeys as the screens show it (never the key itself).</summary>
/// <param name="BackedUp">The passkey is kept by a password manager or platform account that syncs it to the person's other devices.</param>
public sealed record PasskeyDto(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, bool BackedUp, uint Version);

/// <summary>The signed-in user's passkeys, and until when this session may add one without
/// signing in again (null: it must sign in again first).</summary>
public sealed record MyPasskeysResponse(IReadOnlyList<PasskeyDto> Items, DateTimeOffset? CanAddUntil, int Limit);

/// <summary>WebAuthn's relying party (the site): <c>id</c> is the host name passkeys are made for.</summary>
public sealed record PasskeyRelyingParty(string Id, string Name);

/// <summary>WebAuthn's user entity: <c>id</c> is the user handle (base64url), <c>name</c> the e-mail.</summary>
public sealed record PasskeyUserEntity(string Id, string Name, string DisplayName);

/// <summary>An accepted key type: <c>alg</c> is a COSE algorithm (-7 ES256, -257 RS256).</summary>
public sealed record PasskeyParameter(string Type, int Alg);

/// <summary>A credential the device must not create again (base64url id).</summary>
public sealed record PasskeyDescriptor(string Type, string Id, IReadOnlyList<string>? Transports);

/// <summary>What kind of authenticator: a discoverable credential (a passkey), verified by the person.</summary>
public sealed record PasskeySelection(string ResidentKey, bool RequireResidentKey, string UserVerification);

/// <summary>Options for <c>navigator.credentials.create()</c>, in WebAuthn's JSON form (binary values base64url).</summary>
public sealed record PasskeyCreationOptions(
    string Challenge,
    PasskeyRelyingParty Rp,
    PasskeyUserEntity User,
    IReadOnlyList<PasskeyParameter> PubKeyCredParams,
    IReadOnlyList<PasskeyDescriptor> ExcludeCredentials,
    int Timeout,
    PasskeySelection AuthenticatorSelection,
    string Attestation);

/// <summary>A new passkey: the browser's answer to the creation options (base64url), and the
/// person's name for it.</summary>
/// <param name="Name">The person's label for the passkey, 1 to 100 characters ("Work laptop").</param>
/// <param name="ClientDataJson">response.clientDataJSON, base64url.</param>
/// <param name="AttestationObject">response.attestationObject, base64url.</param>
/// <param name="Transports">response.getTransports(): how the browser reaches the authenticator.</param>
public sealed record RegisterPasskeyRequest(string? Name, string? ClientDataJson, string? AttestationObject, IReadOnlyList<string>? Transports);

/// <summary>A new name for a passkey; <c>version</c> is the one the screen read (optimistic concurrency).</summary>
public sealed record RenamePasskeyRequest(string? Name, uint? Version);

/// <summary>How many passkeys were removed.</summary>
public sealed record PasskeysRemovedResponse(int Removed);

/// <summary>
/// Passkeys: a signed-in user adds, renames and removes their own; an administrator sees another
/// user's and can remove them all (a lost device, a person leaving). Signing in with one goes
/// through <c>POST /api/auth/sign-in</c> (its <c>passkey</c> field), with a challenge from the
/// anonymous <c>GET /api/auth/session</c>.
/// </summary>
internal static class PasskeyEndpoints
{
    public const int MaxNameLength = 100;

    private static readonly string[] KnownTransports = ["ble", "hybrid", "internal", "nfc", "smart-card", "usb"];

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/me/passkeys", Mine)
            .WithName("identity.me.passkeys")
            .WithSummary("The signed-in user's passkeys, and until when this session may add one (a passkey is added only within a few minutes of signing in).")
            .RequirePermission(IdentityPermissions.ProfileUpdate);

        group.MapPost("/me/passkeys/options", CreationOptions)
            .WithName("identity.me.passkeys.options")
            .WithSummary("Options for navigator.credentials.create(): a fresh challenge bound to this session, the user handle and the passkeys the device must not make again. 403 auth.recentSignInRequired when the session was signed in too long ago.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(IdentityPermissions.ProfileUpdate);

        group.MapPost("/me/passkeys", Register)
            .WithName("identity.me.passkeys.register")
            .WithSummary("Add a passkey: the browser's answer to the creation options. The answer must be for this site, made by a verified person, with an ES256 or RS256 key, and answer a challenge issued to this session.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(IdentityPermissions.ProfileUpdate);

        group.MapPut("/me/passkeys/{id:guid}", Rename)
            .WithName("identity.me.passkeys.rename")
            .WithSummary("Rename one of the signed-in user's passkeys.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(IdentityPermissions.ProfileUpdate);

        group.MapDelete("/me/passkeys/{id:guid}", Remove)
            .WithName("identity.me.passkeys.remove")
            .WithSummary("Remove one of the signed-in user's passkeys: it no longer signs in (the device may still list it until the person deletes it there).")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(IdentityPermissions.ProfileUpdate);

        group.MapGet("/users/{id:guid}/passkeys", OfUser)
            .WithName("identity.users.passkeys")
            .WithSummary("Another user's passkeys (names and dates, never keys).")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(IdentityPermissions.UsersRead);

        group.MapDelete("/users/{id:guid}/passkeys", RemoveOfUser)
            .WithName("identity.users.passkeys.remove")
            .WithSummary("Remove every passkey of another user (a lost device, a person leaving): only for users whose access is within the caller's own, never on oneself.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(IdentityPermissions.UsersUpdate);
    }

    /// <summary>The user handle of a user: the workspace id then the user id (32 bytes), so a
    /// sign-in knows where to look for the passkey before any workspace is bound.</summary>
    public static byte[] UserHandle(Guid tenantId, Guid userId)
    {
        var handle = new byte[32];
        tenantId.TryWriteBytes(handle.AsSpan(0, 16), bigEndian: true, out _);
        userId.TryWriteBytes(handle.AsSpan(16, 16), bigEndian: true, out _);
        return handle;
    }

    private static async Task<Ok<MyPasskeysResponse>> Mine(IdentityDbContext db, ICurrentUser caller, IOptions<AuthOptions> options, TimeProvider time, CancellationToken cancellationToken) =>
        TypedResults.Ok(new MyPasskeysResponse(await ListAsync(db, caller.UserId, cancellationToken), await CanAddUntilAsync(db, caller, options.Value, time, cancellationToken),
            options.Value.PasskeysPerUser));

    private static async Task<Results<Ok<PasskeyCreationOptions>, ProblemHttpResult>> CreationOptions(
        IdentityDbContext db, ICurrentUser caller, PasskeyChallenges challenges, IOptions<AuthOptions> options, TimeProvider time, HttpContext http, CancellationToken cancellationToken)
    {
        if (await CanAddUntilAsync(db, caller, options.Value, time, cancellationToken) is null)
        {
            return Problems.Forbidden(http, "auth.recentSignInRequired");
        }
        var existing = await db.Passkeys.AsNoTracking().Where(p => p.UserId == caller.UserId)
            .Select(p => new { p.CredentialId, p.Transports }).ToListAsync(cancellationToken);
        if (existing.Count >= options.Value.PasskeysPerUser)
        {
            return Problems.Conflict(http, "identity.passkeyLimit");
        }
        var user = await db.Users.AsNoTracking().Where(u => u.Id == caller.UserId).Select(u => new { u.Email, u.DisplayName }).SingleAsync(cancellationToken);
        return TypedResults.Ok(new PasskeyCreationOptions(
            WebAuthn.ToBase64Url(challenges.Issue(PasskeyChallenges.Purpose.Register, caller.SessionId)),
            new PasskeyRelyingParty(challenges.RpId(http), "ERP"),
            new PasskeyUserEntity(WebAuthn.ToBase64Url(UserHandle(caller.TenantId, caller.UserId)), user.Email, user.DisplayName),
            CoseAlgorithms.Accepted.Select(a => new PasskeyParameter("public-key", a)).ToList(),
            existing.Select(p => new PasskeyDescriptor("public-key", WebAuthn.ToBase64Url(p.CredentialId), p.Transports.Count > 0 ? p.Transports : null)).ToList(),
            challenges.TimeoutMilliseconds,
            new PasskeySelection("required", true, "required"),
            "none"));
    }

    private static async Task<Results<Created<PasskeyDto>, ProblemHttpResult>> Register(
        RegisterPasskeyRequest request, IdentityDbContext db, ICurrentUser caller, PasskeyChallenges challenges, IOptions<AuthOptions> options,
        TimeProvider time, HttpContext http, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var validator = new Validator(http)
            .Required("name", name).MaxLength("name", name, MaxNameLength)
            .Required("clientDataJson", request.ClientDataJson)
            .Required("attestationObject", request.AttestationObject)
            .Must(request.Transports is not { Count: > 8 }, "transports", "identityTooManyTransports", 8);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        if (await CanAddUntilAsync(db, caller, options.Value, time, cancellationToken) is null)
        {
            return Problems.Forbidden(http, "auth.recentSignInRequired");
        }
        var clientData = WebAuthn.FromBase64Url(request.ClientDataJson, 4096);
        var attestation = WebAuthn.FromBase64Url(request.AttestationObject, 16384);
        AuthenticatorData? data = null;
        var valid = clientData is not null && attestation is not null &&
                    WebAuthn.TryReadClientData(clientData, WebAuthn.CreationType, origin => challenges.OriginAllowed(origin, http), out var challenge) &&
                    challenges.Verify(challenge, PasskeyChallenges.Purpose.Register, caller.SessionId, out _) &&
                    Cbor.TryReadWhole(attestation, out var parsed) && parsed is Dictionary<object, object?> map &&
                    map.GetValueOrDefault("authData") is byte[] authData &&
                    (data = WebAuthn.ReadAuthenticatorData(authData)) is { CredentialId: not null, PublicKey: not null, Algorithm: not null } &&
                    data.Has(AuthenticatorData.AttestedCredentialData) &&
                    WebAuthn.IsForRelyingParty(data, challenges.RpId(http));
        if (!valid || data is null)
        {
            return new Validator(http).Add("attestationObject", "identityPasskeyInvalid").ToResult();
        }
        if (await db.Passkeys.CountAsync(p => p.UserId == caller.UserId, cancellationToken) >= options.Value.PasskeysPerUser)
        {
            return Problems.Conflict(http, "identity.passkeyLimit");
        }
        if (await db.Passkeys.AnyAsync(p => p.CredentialId == data.CredentialId, cancellationToken))
        {
            return Problems.Conflict(http, "identity.passkeyExists");
        }
        var passkey = new Passkey
        {
            UserId = caller.UserId,
            CredentialId = data.CredentialId!,
            PublicKey = data.PublicKey!,
            Algorithm = data.Algorithm!.Value,
            Name = name!,
            SignCount = data.SignCount,
            BackupEligible = data.Has(AuthenticatorData.BackupEligibleFlag),
            BackedUp = data.Has(AuthenticatorData.BackedUpFlag),
            Transports = (request.Transports ?? []).Select(t => t.Trim().ToLowerInvariant()).Where(KnownTransports.Contains).Distinct().Order(StringComparer.Ordinal).ToList(),
        };
        db.Passkeys.Add(passkey);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/identity/me/passkeys/{passkey.Id}", ToDto(passkey));
    }

    private static async Task<Results<Ok<PasskeyDto>, ProblemHttpResult>> Rename(
        Guid id, RenamePasskeyRequest request, IdentityDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var validator = new Validator(http).Required("name", name).MaxLength("name", name, MaxNameLength);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var passkey = await db.Passkeys.SingleOrDefaultAsync(p => p.Id == id && p.UserId == caller.UserId, cancellationToken);
        if (passkey is null)
        {
            return Problems.NotFound(http);
        }
        if (request.Version is { } version && version != passkey.Version)
        {
            return Problems.Conflict(http, "concurrency");
        }
        passkey.Name = name!;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(passkey));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Remove(Guid id, IdentityDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var passkey = await db.Passkeys.SingleOrDefaultAsync(p => p.Id == id && p.UserId == caller.UserId, cancellationToken);
        if (passkey is null)
        {
            return Problems.NotFound(http);
        }
        db.Passkeys.Remove(passkey);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<List<PasskeyDto>>, ProblemHttpResult>> OfUser(Guid id, IdentityDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, cancellationToken))
        {
            return Problems.NotFound(http);
        }
        return TypedResults.Ok(await ListAsync(db, id, cancellationToken));
    }

    private static async Task<Results<Ok<PasskeysRemovedResponse>, ProblemHttpResult>> RemoveOfUser(
        Guid id, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, HttpContext http, CancellationToken cancellationToken)
    {
        if (await UserEndpoints.TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)
        {
            return problem;
        }
        // Tracked removal, so the audit trail records each passkey removed with its owner.
        var passkeys = await db.Passkeys.Where(p => p.UserId == id).ToListAsync(cancellationToken);
        db.Passkeys.RemoveRange(passkeys);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new PasskeysRemovedResponse(passkeys.Count));
    }

    private static Task<List<PasskeyDto>> ListAsync(IdentityDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.Passkeys.AsNoTracking().Where(p => p.UserId == userId)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Select(p => new PasskeyDto(p.Id, p.Name, p.CreatedAt, p.LastUsedAt, p.BackedUp, p.Version))
            .ToListAsync(cancellationToken);

    /// <summary>Until when the caller's session may add a passkey: its sign-in moment plus
    /// <see cref="AuthOptions.PasskeyRecentSignInMinutes"/>; null once that has passed.</summary>
    private static async Task<DateTimeOffset?> CanAddUntilAsync(IdentityDbContext db, ICurrentUser caller, AuthOptions options, TimeProvider time, CancellationToken cancellationToken)
    {
        var signedInAt = await db.Sessions.AsNoTracking().Where(s => s.Id == caller.SessionId).Select(s => (DateTimeOffset?)s.CreatedAt).SingleOrDefaultAsync(cancellationToken);
        if (signedInAt is null)
        {
            return null;
        }
        var until = signedInAt.Value.AddMinutes(Math.Clamp(options.PasskeyRecentSignInMinutes, 1, 60));
        return until > time.GetUtcNow() ? until : null;
    }

    private static PasskeyDto ToDto(Passkey p) => new(p.Id, p.Name, p.CreatedAt, p.LastUsedAt, p.BackedUp, p.Version);
}
