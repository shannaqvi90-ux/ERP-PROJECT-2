using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>Whether a tenant's own rows hold a text, read with the superuser at the moment of the
/// question (a derived label such as a numbering sequence's "type · company name" that tenant A's
/// own valid writes created after tenant B's markers were read).</summary>
internal static class AttackerRows
{
    public static async Task<bool> HoldsAsync(ErpTestEnvironment env, Guid tenant, string text)
    {
        await using var admin = await env.OpenAdminAsync();
        foreach (var table in await DbCatalog.TenantTablesAsync(admin))
        {
            var found = await DbCatalog.ReadAsync(admin,
                $"SELECT 1 FROM {table.Qualified} t WHERE t.tenant_id = @t AND position(lower(@m) in lower(t::text)) > 0 LIMIT 1",
                r => r.GetInt32(0), ("t", tenant), ("m", text));
            if (found.Count > 0)
            {
                return true;
            }
        }
        return false;
    }
}
