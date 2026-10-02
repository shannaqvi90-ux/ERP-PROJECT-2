using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// Everything that identifies one tenant's data, read with the superuser: every uuid primary key
/// of every tenant table, its canary, and a checksum of every tenant table's rows. Volatile
/// columns (reviewed in tests/Gates/g1-volatile-columns.txt) are left out of the checksum.
/// </summary>
public sealed class TenantSnapshot
{
    public required Guid TenantId { get; init; }
    public required string? Canary { get; init; }
    public required string Code { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<Guid>> IdsByTable { get; init; }
    public required IReadOnlyDictionary<string, string> Checksums { get; init; }

    public IEnumerable<Guid> AllIds => IdsByTable.Values.SelectMany(v => v).Append(TenantId).Distinct();

    /// <summary>Strings that must never appear in a response to another tenant.</summary>
    public IReadOnlyList<string> Markers =>
        AllIds.Select(id => id.ToString()).Concat(Canary is null ? [] : [Canary]).Distinct().ToList();

    public static async Task<TenantSnapshot> TakeAsync(ErpTestEnvironment env, Guid tenantId, string? canary, string code)
    {
        await using var admin = await env.OpenAdminAsync();
        var volatileColumns = Repo.ReadReviewedList("tests/Gates/g1-volatile-columns.txt").Select(v => v.Entry).ToHashSet();
        var ids = new Dictionary<string, IReadOnlyList<Guid>>();
        var checksums = new Dictionary<string, string>();
        foreach (var table in await DbCatalog.TenantTablesAsync(admin))
        {
            var columns = await DbCatalog.ColumnsAsync(admin, table);
            if (columns.Any(c => c.Name == "id" && c.Type == "uuid"))
            {
                ids[table.Qualified] = await DbCatalog.ReadAsync(admin,
                    $"SELECT id FROM {table.Qualified} WHERE tenant_id = @t ORDER BY id", r => r.GetGuid(0), ("t", tenantId));
            }
            var stable = columns.Where(c => !volatileColumns.Contains($"{table.Qualified}.{c.Name}")).Select(c => c.Name);
            checksums[table.Qualified] = await DbCatalog.ScalarAsync<string>(admin,
                $"SELECT count(*)::text || ':' || coalesce(md5(string_agg(row_text, '|' ORDER BY row_text)), '') " +
                $"FROM (SELECT ROW({string.Join(", ", stable)})::text AS row_text FROM {table.Qualified} WHERE tenant_id = @t) s",
                ("t", tenantId));
        }
        return new TenantSnapshot { TenantId = tenantId, Canary = canary, Code = code, IdsByTable = ids, Checksums = checksums };
    }

    /// <summary>Tables whose rows changed between two snapshots.</summary>
    public static IReadOnlyList<string> Differences(TenantSnapshot before, TenantSnapshot after) =>
        before.Checksums.Keys.Union(after.Checksums.Keys)
            .Where(t => before.Checksums.GetValueOrDefault(t) != after.Checksums.GetValueOrDefault(t))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>The first marker found in a text, or null.</summary>
    public string? FindMarker(string text)
    {
        foreach (var marker in Markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return marker;
            }
        }
        return null;
    }
}
