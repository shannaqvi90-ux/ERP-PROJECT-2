using Erp.Kernel.Data;
using Erp.Kernel.Security;
using Erp.Modules.Tenancy.Contracts;
using Npgsql;

namespace Erp.Modules.Identity.Auth;

/// <summary>Resolves a session token hash with the reviewed <c>identity.resolve_session</c>
/// lookup, then binds the unit of work to the session's tenant and loads the user and the
/// permissions their workspace-wide roles grant (with their company roles, which
/// <see cref="SessionGrants"/> adds for the working company once the scope is bound).</summary>
internal sealed class SessionResolver(ErpDbSession session, SessionGrants grants, ITenantDirectory tenants, IEnumerable<ISessionPrefetch> prefetch) : ISessionResolver
{
    public async Task<ResolvedSession?> ResolveAsync(byte[] tokenHash, CancellationToken cancellationToken)
    {
        var connection = await session.OpenUnboundAsync(cancellationToken);
        Guid sessionId, tenantId, userId;
        DateTimeOffset expiresAt;
        await using (var command = new NpgsqlCommand(
            "SELECT session_id, tenant_id, user_id, expires_at FROM identity.resolve_session(@hash)", connection))
        {
            command.Parameters.AddWithValue("hash", tokenHash);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }
            sessionId = reader.GetGuid(0);
            tenantId = reader.GetGuid(1);
            userId = reader.GetGuid(2);
            expiresAt = reader.GetFieldValue<DateTimeOffset>(3);
        }

        // The tenant settings, the user with every role they hold (SessionGrants.Statement), the
        // workspace's status and the user's company access go to the server in one round trip.
        await session.BeginAsync(tenantId, userId, "user", cancellationToken, prefetch.ToList());
        var (user, held) = await grants.ReadAsync(userId, cancellationToken);
        if (user is not { IsActive: true } || await tenants.GetCurrentAsync(cancellationToken) is null)
        {
            // Deactivated user or suspended workspace: the session no longer counts.
            await session.RollbackAsync(cancellationToken);
            return null;
        }
        return new ResolvedSession(sessionId, tenantId, userId, user.Email, user.DisplayName, user.Language, expiresAt, held.Everywhere.ToList());
    }
}
