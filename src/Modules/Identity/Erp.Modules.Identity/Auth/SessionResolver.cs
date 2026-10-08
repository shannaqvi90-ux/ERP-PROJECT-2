using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Modules.Identity.Auth;

/// <summary>Resolves a session token hash with the reviewed <c>identity.resolve_session</c>
/// lookup, then binds the unit of work to the session's tenant and loads the user and the
/// permissions their roles grant.</summary>
internal sealed class SessionResolver(ErpDbSession session, IdentityDbContext db, ModuleCatalog catalog, ITenantDirectory tenants) : ISessionResolver
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

        await session.BeginAsync(tenantId, userId, "user", cancellationToken);
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new { u.Email, u.DisplayName, u.Language })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null || await tenants.GetCurrentAsync(cancellationToken) is null)
        {
            // Deactivated user or suspended workspace: the session no longer counts.
            await session.RollbackAsync(cancellationToken);
            return null;
        }
        var permissions = await PermissionQueries.ForUserAsync(db, userId, catalog, cancellationToken);
        return new ResolvedSession(sessionId, tenantId, userId, user.Email, user.DisplayName, user.Language, expiresAt, permissions);
    }
}

internal static class PermissionQueries
{
    /// <summary>The union of permissions the user's roles grant, limited to permissions that
    /// still exist in the catalogue.</summary>
    public static async Task<IReadOnlySet<string>> ForUserAsync(IdentityDbContext db, Guid userId, ModuleCatalog catalog, CancellationToken cancellationToken)
    {
        var grants = await (
                from userRole in db.UserRoles
                where userRole.UserId == userId
                join role in db.Roles on userRole.RoleId equals role.Id
                select role.Permissions)
            .ToListAsync(cancellationToken);
        return Known(grants, catalog);
    }

    /// <summary>The permissions in <paramref name="grants"/> that still exist in the catalogue.</summary>
    public static IReadOnlySet<string> Known(IEnumerable<IEnumerable<string>> grants, ModuleCatalog catalog) =>
        grants.SelectMany(p => p).Where(catalog.IsPermission).ToHashSet(StringComparer.Ordinal);
}
