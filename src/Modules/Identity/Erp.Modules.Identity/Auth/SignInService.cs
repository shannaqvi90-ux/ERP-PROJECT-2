using System.Net;
using System.Net.Sockets;
using Erp.Kernel.Data;
using Erp.Kernel.Security;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Erp.Modules.Identity.Auth;

internal abstract record SignInOutcome
{
    public sealed record Failed : SignInOutcome;

    public sealed record ChooseWorkspace(IReadOnlyList<WorkspaceChoice> Workspaces) : SignInOutcome;

    /// <summary>The password was a one-time set-up code (or an administrator asked for a change):
    /// a new password is needed before a session starts.</summary>
    public sealed record PasswordChangeRequired : SignInOutcome;

    /// <summary>The new password was refused (a validation code and its arguments).</summary>
    public sealed record NewPasswordInvalid(string Code, object?[] Args) : SignInOutcome;

    public sealed record Succeeded(Guid TenantId, Guid UserId, Guid SessionId, string Token, DateTimeOffset ExpiresAt) : SignInOutcome;
}

public sealed record WorkspaceChoice(string Code, string NameEn, string NameAr);

/// <summary>
/// E-mail and password sign-in. The e-mail is unique within a tenant, not across tenants, so no
/// tenant can learn which addresses another tenant uses. The application never reads a password
/// hash: the reviewed <c>identity.verify_sign_in</c> function first gives the hashing parameters
/// (algorithm, cost, salt) of each account with the address (or a decoy), the application derives
/// the typed password under each, and the function compares those proofs with the stored hashes
/// and returns only the workspaces whose password matched. One match signs in; several (the same
/// person in two workspaces with the same password) ask which workspace. Failed attempts pause
/// only the client that made them, on only that account; a browser that signed in to the account
/// before is a client of its own (<see cref="TrustedDevices"/>), apart from its network address.
/// Failures never say whether the address exists, whether the client is paused or what the policy is.
/// </summary>
internal sealed class SignInService(
    ErpDbSession session,
    IdentityDbContext db,
    IServiceScopeFactory scopes,
    ITenantDirectory tenants,
    TrustedDevices devices,
    Passkeys.PasskeyChallenges challenges,
    IOptions<AuthOptions> options,
    TimeProvider time,
    ILogger<SignInService> logger)
{
    private sealed record Match(string Challenge, Guid TenantId);

    public async Task<SignInOutcome> SignInAsync(string email, string password, string? newPassword, string? workspace, HttpContext http, CancellationToken cancellationToken)
    {
        // A request that arrived with another (valid) session is already bound to that tenant.
        await session.RollbackAsync(cancellationToken);
        var connection = await session.OpenUnboundAsync(cancellationToken);
        var client = ClientOf(http);
        // A browser that signed in to this account before counts its failures as itself, not as
        // its network address (shared by everyone behind the same NAT or proxy).
        if (devices.SourceFor(http, email) is { } device)
        {
            client = client with { Source = device };
        }

        var challenges = new List<string>();
        await using (var command = Lookup(connection, email, null, client))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                challenges.Add(reader.GetString(0));
            }
        }
        // The same work for every challenge, real or decoy, so timing does not reveal accounts.
        var proofs = challenges.Select(c => PasswordHasher.Prove(password, c)).OfType<string>().ToArray();

        var matches = new List<Match>();
        await using (var command = Lookup(connection, email, proofs, client))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                matches.Add(new Match(reader.GetString(0), reader.GetGuid(1)));
            }
        }
        if (matches.Count == 0)
        {
            return new SignInOutcome.Failed();
        }

        var chosen = matches[0];
        if (matches.Count > 1)
        {
            var choices = new List<(WorkspaceChoice Choice, Match Match)>();
            foreach (var match in matches)
            {
                var info = await DescribeTenantAsync(match.TenantId, cancellationToken);
                if (info is not null)
                {
                    choices.Add((new WorkspaceChoice(info.Code, info.NameEn, info.NameAr), match));
                }
            }
            if (choices.Count == 0)
            {
                return new SignInOutcome.Failed();
            }
            var pick = choices.FirstOrDefault(c => string.Equals(c.Choice.Code, workspace?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (pick.Match is null)
            {
                if (choices.Count > 1)
                {
                    return new SignInOutcome.ChooseWorkspace(choices.Select(c => c.Choice).OrderBy(c => c.Code, StringComparer.Ordinal).ToList());
                }
                pick = choices[0];
            }
            chosen = pick.Match;
        }

        session.CorrelationId ??= http.TraceIdentifier;
        // Find the account by e-mail inside the chosen workspace, then work as that user.
        await session.BeginAsync(chosen.TenantId, null, "system", cancellationToken);
        if (await tenants.GetCurrentAsync(cancellationToken) is null)
        {
            // Suspended workspace: answer like any other failure.
            await session.RollbackAsync(cancellationToken);
            return new SignInOutcome.Failed();
        }
        var normalized = email.Trim().ToLowerInvariant();
        var userId = await db.Users.Where(u => u.EmailNormalized == normalized).Select(u => u.Id).SingleAsync(cancellationToken);
        await session.RollbackAsync(cancellationToken);
        await session.BeginAsync(chosen.TenantId, userId, "user", cancellationToken);
        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);

        var mustChange = await db.Credentials.Where(c => c.Id == user.Id).Select(c => c.MustChange).SingleAsync(cancellationToken);
        var now = time.GetUtcNow();
        if (newPassword is null && mustChange)
        {
            return new SignInOutcome.PasswordChangeRequired();
        }
        if (newPassword is not null)
        {
            if (Passwords.Problem(newPassword) is { } problem)
            {
                return new SignInOutcome.NewPasswordInvalid(problem.Code, problem.Args);
            }
            if (newPassword == password)
            {
                return new SignInOutcome.NewPasswordInvalid("passwordUnchanged", []);
            }
            await Passwords.SetAsync(db, user.Id, newPassword, mustChange: false, expiresAt: null, now, user.Id, cancellationToken);
            // A new password ends every other session of the account.
            await db.Sessions.Where(s => s.UserId == user.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
        }
        else if (PasswordHasher.IsOutdated(chosen.Challenge))
        {
            await Passwords.SetAsync(db, user.Id, password, mustChange: false, expiresAt: null, now, user.Id, cancellationToken);
        }

        return await StartSessionAsync(user, client, email, now, http, cancellationToken);
    }

    /// <summary>
    /// Sign-in with a passkey. The user handle the device returns names the account's workspace
    /// and user (<see cref="Passkeys.PasskeyEndpoints.UserHandle"/>), as an e-mail names an
    /// account: that workspace is bound only to find the passkey there and read its public key,
    /// and nothing is answered or kept from it unless the device's signature over this
    /// deployment's own challenge verifies with that key. A handle naming another workspace or
    /// user, an unknown credential, a removed passkey, an inactive account or a suspended
    /// workspace all fail with the same answer. Each challenge is answered once: the passkey keeps
    /// the issue time of the last challenge it answered.
    /// </summary>
    public async Task<SignInOutcome> SignInWithPasskeyAsync(PasskeyAssertion assertion, HttpContext http, CancellationToken cancellationToken)
    {
        await session.RollbackAsync(cancellationToken);
        var credentialId = Passkeys.WebAuthn.FromBase64Url(assertion.CredentialId, 1023);
        var clientData = Passkeys.WebAuthn.FromBase64Url(assertion.ClientDataJson, 4096);
        var authenticatorData = Passkeys.WebAuthn.FromBase64Url(assertion.AuthenticatorData, 2048);
        var signature = Passkeys.WebAuthn.FromBase64Url(assertion.Signature, 1024);
        var handle = Passkeys.WebAuthn.FromBase64Url(assertion.UserHandle, 64);
        if (credentialId is not { Length: >= 16 } || clientData is null || authenticatorData is null || signature is null || handle is not { Length: 32 } ||
            !Passkeys.WebAuthn.TryReadClientData(clientData, Passkeys.WebAuthn.AssertionType, origin => challenges.OriginAllowed(origin, http), out var challenge) ||
            !challenges.Verify(challenge, Passkeys.PasskeyChallenges.Purpose.SignIn, null, out var issuedAt) ||
            Passkeys.WebAuthn.ReadAuthenticatorData(authenticatorData) is not { } data ||
            !Passkeys.WebAuthn.IsForRelyingParty(data, challenges.RpId(http)))
        {
            return new SignInOutcome.Failed();
        }
        var tenantId = new Guid(handle.AsSpan(0, 16), bigEndian: true);
        var userId = new Guid(handle.AsSpan(16, 16), bigEndian: true);
        var client = ClientOf(http);

        session.CorrelationId ??= http.TraceIdentifier;
        // The workspace the device names, to find the passkey's public key there.
        await session.BeginAsync(tenantId, null, "system", cancellationToken);
        if (await tenants.GetCurrentAsync(cancellationToken) is null)
        {
            await session.RollbackAsync(cancellationToken);
            return new SignInOutcome.Failed();
        }
        var key = await (
                from p in db.Passkeys.AsNoTracking()
                join u in db.Users.AsNoTracking() on p.UserId equals u.Id
                where p.UserId == userId && p.CredentialId == credentialId && u.IsActive
                select new { p.Id, p.PublicKey, p.Algorithm, p.SignCount, u.Email })
            .SingleOrDefaultAsync(cancellationToken);
        await session.RollbackAsync(cancellationToken);
        if (key is null || !Passkeys.WebAuthn.VerifySignature(key.PublicKey, key.Algorithm, authenticatorData, clientData, signature))
        {
            logger.LogInformation("Passkey sign-in refused from {Source}", client.Source);
            return new SignInOutcome.Failed();
        }

        // Proven: work as that user.
        await session.BeginAsync(tenantId, userId, "user", cancellationToken);
        var now = time.GetUtcNow();
        var counter = (long)data.SignCount;
        // Once and in order: an answer to the same or an older challenge is a replay; a signature
        // counter that does not move forward (when the device keeps one) is a copied key.
        var used = await db.Passkeys
            .Where(p => p.Id == key.Id && (p.LastChallengeAt == null || p.LastChallengeAt < issuedAt) &&
                        ((p.SignCount == 0 && counter == 0) || counter > p.SignCount))
            .ExecuteUpdateAsync(set => set
                .SetProperty(p => p.LastUsedAt, now)
                .SetProperty(p => p.LastChallengeAt, issuedAt)
                .SetProperty(p => p.SignCount, counter)
                .SetProperty(p => p.BackedUp, data.Has(Passkeys.AuthenticatorData.BackedUpFlag)), cancellationToken);
        if (used == 0)
        {
            logger.LogWarning("Passkey {PasskeyId} answered an old challenge or its counter went back; sign-in refused", key.Id);
            return new SignInOutcome.Failed();
        }
        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        return await StartSessionAsync(user, client, key.Email, now, http, cancellationToken);
    }

    /// <summary>The checked account signs in: its sign-in moment, a new session and the sign-in
    /// history entry, in the request's transaction bound to that user.</summary>
    private async Task<SignInOutcome> StartSessionAsync(User user, Client client, string email, DateTimeOffset now, HttpContext http, CancellationToken cancellationToken)
    {
        // The sign-in moment is written in place, not through the tracked record: two sign-ins of
        // one account at the same moment (two tabs, a browser and an API client) both succeed
        // instead of the later one failing on the record's version. The audit trigger still
        // records the change, stamped as the user's own like any edit of the record.
        await db.Users
            .Where(u => u.Id == user.Id && (u.LastSignInAt == null || u.LastSignInAt < now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LastSignInAt, now)
                .SetProperty(u => u.UpdatedAt, now)
                .SetProperty(u => u.UpdatedBy, (Guid?)user.Id), cancellationToken);
        var token = SessionTokens.Generate();
        var expiresAt = now.AddHours(options.Value.SessionHours);
        var row = new Session
        {
            UserId = user.Id,
            TokenHash = SessionTokens.Hash(token),
            ExpiresAt = expiresAt,
            IpAddress = client.Address,
            UserAgent = client.UserAgent,
        };
        db.Sessions.Add(row);
        db.SignInAttempts.Add(new SignInAttempt
        {
            UserId = user.Id,
            OccurredAt = now,
            Outcome = SignInOutcomes.Succeeded,
            Source = client.Source,
            IpAddress = client.Address,
            UserAgent = client.UserAgent,
            SessionId = row.Id,
        });
        await db.SaveChangesAsync(cancellationToken);
        devices.Remember(http, email, options.Value.AlwaysSecureCookie || http.Request.IsHttps);
        logger.LogInformation("User {UserId} signed in to tenant {TenantId}", user.Id, user.TenantId);
        return new SignInOutcome.Succeeded(user.TenantId, user.Id, row.Id, token, expiresAt);
    }

    private NpgsqlCommand Lookup(NpgsqlConnection connection, string email, string[]? proofs, Client client)
    {
        var command = new NpgsqlCommand(
            "SELECT challenge, tenant_id FROM identity.verify_sign_in(@email, @proofs, @source, @ip, @agent, @threshold, @minutes)", connection);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.Add(new NpgsqlParameter("proofs", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)proofs ?? DBNull.Value });
        command.Parameters.AddWithValue("source", client.Source);
        command.Parameters.Add(new NpgsqlParameter("ip", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)client.Address ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("agent", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)client.UserAgent ?? DBNull.Value });
        command.Parameters.AddWithValue("threshold", options.Value.LockoutThreshold);
        command.Parameters.AddWithValue("minutes", options.Value.LockoutMinutes);
        return command;
    }

    /// <summary>The client making the attempt. Failures are counted per account and per
    /// <see cref="Client.Source"/>: the IPv4 address, or the /64 network of an IPv6 address (one
    /// subscriber's allocation, so rotating addresses inside it does not reset the count).</summary>
    internal sealed record Client(string Source, string? Address, string? UserAgent);

    internal static Client ClientOf(HttpContext http)
    {
        var address = http.Connection.RemoteIpAddress;
        if (address is { IsIPv4MappedToIPv6: true })
        {
            address = address.MapToIPv4();
        }
        return new Client(SourceOf(address), address?.ToString(), Truncate(http.Request.Headers.UserAgent.ToString(), 400));
    }

    internal static string SourceOf(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes) + "/64";
        }
        return address.ToString();
    }

    private async Task<TenantInfo?> DescribeTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var tenantSession = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await tenantSession.BeginAsync(tenantId, null, "system", cancellationToken);
        return await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().GetCurrentAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
