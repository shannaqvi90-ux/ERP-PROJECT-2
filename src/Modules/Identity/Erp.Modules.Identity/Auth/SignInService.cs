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

    public sealed record Succeeded(Guid TenantId, Guid UserId, Guid SessionId, string Token, DateTimeOffset ExpiresAt) : SignInOutcome;
}

public sealed record WorkspaceChoice(string Code, string NameEn, string NameAr);

/// <summary>
/// E-mail and password sign-in. The e-mail is unique within a tenant, not across tenants, so no
/// tenant can learn which addresses another tenant uses. The reviewed
/// <c>identity.resolve_login</c> lookup returns every account with the address; the password is
/// verified against each. One match signs in; several matches (the same person in two
/// workspaces with the same password) ask which workspace, listing only workspaces whose
/// password matched. Failures never say whether the address exists or the account is paused.
/// </summary>
internal sealed class SignInService(
    ErpDbSession session,
    IdentityDbContext db,
    IServiceScopeFactory scopes,
    ITenantDirectory tenants,
    IOptions<AuthOptions> options,
    TimeProvider time,
    ILogger<SignInService> logger)
{
    private sealed record Candidate(Guid TenantId, Guid UserId, string PasswordHash, bool IsActive, DateTimeOffset? LockoutUntil);

    public async Task<SignInOutcome> SignInAsync(string email, string password, string? workspace, HttpContext http, CancellationToken cancellationToken)
    {
        // A request that arrived with another (valid) session is already bound to that tenant.
        await session.RollbackAsync(cancellationToken);
        var connection = await session.OpenUnboundAsync(cancellationToken);
        var candidates = new List<Candidate>();
        await using (var command = new NpgsqlCommand(
            "SELECT tenant_id, user_id, password_hash, is_active, lockout_until FROM identity.resolve_login(@email)", connection))
        {
            command.Parameters.AddWithValue("email", email);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(new Candidate(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetBoolean(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4)));
            }
        }

        var now = time.GetUtcNow();
        var matched = new List<(Candidate Candidate, bool NeedsRehash)>();
        if (candidates.Count == 0)
        {
            PasswordHasher.Verify(password, PasswordHasher.Decoy, out _);
        }
        foreach (var candidate in candidates)
        {
            if (!candidate.IsActive || candidate.LockoutUntil > now)
            {
                // Same work as a real check so timing does not reveal the account state.
                PasswordHasher.Verify(password, PasswordHasher.Decoy, out _);
                continue;
            }
            if (PasswordHasher.Verify(password, candidate.PasswordHash, out var needsRehash))
            {
                matched.Add((candidate, needsRehash));
            }
            else
            {
                await RecordFailureAsync(candidate, http.TraceIdentifier, cancellationToken);
            }
        }

        if (matched.Count == 0)
        {
            return new SignInOutcome.Failed();
        }

        var chosen = matched[0];
        if (matched.Count > 1)
        {
            var choices = new List<(WorkspaceChoice Choice, Candidate Candidate, bool NeedsRehash)>();
            foreach (var (candidate, needsRehash) in matched)
            {
                var info = await DescribeTenantAsync(candidate.TenantId, cancellationToken);
                if (info is not null)
                {
                    choices.Add((new WorkspaceChoice(info.Code, info.NameEn, info.NameAr), candidate, needsRehash));
                }
            }
            var pick = choices.FirstOrDefault(c => string.Equals(c.Choice.Code, workspace?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (pick.Candidate is null)
            {
                return new SignInOutcome.ChooseWorkspace(choices.Select(c => c.Choice).OrderBy(c => c.Code, StringComparer.Ordinal).ToList());
            }
            chosen = (pick.Candidate, pick.NeedsRehash);
        }

        session.CorrelationId ??= http.TraceIdentifier;
        await session.BeginAsync(chosen.Candidate.TenantId, chosen.Candidate.UserId, "user", cancellationToken);
        if (await tenants.GetCurrentAsync(cancellationToken) is null)
        {
            // Suspended workspace: answer like any other failure.
            await session.RollbackAsync(cancellationToken);
            return new SignInOutcome.Failed();
        }
        var user = await db.Users.SingleAsync(u => u.Id == chosen.Candidate.UserId, cancellationToken);
        user.FailedSignInCount = 0;
        user.LockoutUntil = null;
        user.LastSignInAt = now;
        if (chosen.NeedsRehash)
        {
            user.PasswordHash = PasswordHasher.Hash(password);
        }
        var token = SessionTokens.Generate();
        var expiresAt = now.AddHours(options.Value.SessionHours);
        var row = new Session
        {
            UserId = user.Id,
            TokenHash = SessionTokens.Hash(token),
            ExpiresAt = expiresAt,
            IpAddress = http.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Truncate(http.Request.Headers.UserAgent.ToString(), 400),
        };
        db.Sessions.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("User {UserId} signed in to tenant {TenantId}", user.Id, user.TenantId);
        return new SignInOutcome.Succeeded(user.TenantId, user.Id, row.Id, token, expiresAt);
    }

    /// <summary>Count a failed attempt in the account's own tenant, pausing sign-in after the
    /// threshold. Committed on its own so the count survives the failed request.</summary>
    private async Task RecordFailureAsync(Candidate candidate, string traceId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var failureSession = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        failureSession.CorrelationId = traceId;
        await failureSession.BeginAsync(candidate.TenantId, null, "system", cancellationToken);
        await using (var command = new NpgsqlCommand("""
            UPDATE identity.users
               SET failed_sign_in_count = CASE WHEN failed_sign_in_count + 1 >= @threshold THEN 0 ELSE failed_sign_in_count + 1 END,
                   lockout_until = CASE WHEN failed_sign_in_count + 1 >= @threshold THEN now() + make_interval(mins => @minutes) ELSE lockout_until END
             WHERE id = @id
            """, failureSession.Connection, failureSession.Transaction))
        {
            command.Parameters.AddWithValue("threshold", options.Value.LockoutThreshold);
            command.Parameters.AddWithValue("minutes", options.Value.LockoutMinutes);
            command.Parameters.AddWithValue("id", candidate.UserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await failureSession.CommitAsync(cancellationToken);
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
