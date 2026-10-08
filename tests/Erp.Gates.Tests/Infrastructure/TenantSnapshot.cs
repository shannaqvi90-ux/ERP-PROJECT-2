using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// Everything that identifies one tenant's data, read with the superuser: every uuid primary key
/// of every tenant table, its canary, and a checksum of every tenant table's rows. Volatile
/// columns (reviewed in tests/Gates/g1-volatile-columns.txt) are left out of the checksum, and so
/// are the rows an attack may add as a record of itself (reviewed in
/// tests/Gates/g1-recorded-rows.txt, for example a failed sign-in kept in the account's own
/// sign-in history). Only the rows matching the reviewed condition are left out; every other row
/// of those tables is still compared.
/// </summary>
public sealed class TenantSnapshot
{
    public required Guid TenantId { get; init; }
    public required string? Canary { get; init; }
    public required string Code { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<Guid>> IdsByTable { get; init; }
    public required IReadOnlyDictionary<string, string> Checksums { get; init; }

    public IEnumerable<Guid> AllIds => IdsByTable.Values.SelectMany(v => v).Append(TenantId).Distinct();

    /// <summary>Strings that must never appear in a response to another tenant (computed once:
    /// a snapshot never changes, and every response of an attack is searched for them).</summary>
    public IReadOnlyList<string> Markers => _markers ??=
        AllIds.Select(id => id.ToString()).Concat(Canary is null ? [] : [Canary]).Distinct().ToList();

    private IReadOnlyList<string>? _markers;
    private MarkerSearch? _search;

    public static async Task<TenantSnapshot> TakeAsync(ErpTestEnvironment env, Guid tenantId, string? canary, string code)
    {
        await using var admin = await env.OpenAdminAsync();
        var volatileColumns = Repo.ReadReviewedList("tests/Gates/g1-volatile-columns.txt").Select(v => v.Entry).ToHashSet();
        var recorded = RecordedRows();
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
            var keep = recorded.TryGetValue(table.Qualified, out var condition) ? $" AND NOT ({condition})" : "";
            checksums[table.Qualified] = await DbCatalog.ScalarAsync<string>(admin,
                $"SELECT count(*)::text || ':' || coalesce(md5(string_agg(row_text, '|' ORDER BY row_text)), '') " +
                $"FROM (SELECT ROW({string.Join(", ", stable)})::text AS row_text FROM {table.Qualified} WHERE tenant_id = @t{keep}) s",
                ("t", tenantId));
        }
        return new TenantSnapshot { TenantId = tenantId, Canary = canary, Code = code, IdsByTable = ids, Checksums = checksums };
    }

    /// <summary>Reviewed rows an attack may add as a record of itself: <c>schema.table: SQL condition</c>.</summary>
    public static IReadOnlyDictionary<string, string> RecordedRows()
    {
        var entries = Repo.ReadReviewedList("tests/Gates/g1-recorded-rows.txt");
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (entry, reason) in entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(reason), $"g1-recorded-rows.txt: {entry} needs a reason");
            var parts = entry.Split(':', 2);
            Assert.True(parts.Length == 2 && parts[1].Trim().Length > 0, $"g1-recorded-rows.txt: '{entry}' must be 'schema.table: condition'");
            map.Add(parts[0].Trim(), parts[1].Trim());
        }
        return map;
    }

    /// <summary>Tables whose rows changed between two snapshots.</summary>
    public static IReadOnlyList<string> Differences(TenantSnapshot before, TenantSnapshot after) =>
        before.Checksums.Keys.Union(after.Checksums.Keys)
            .Where(t => before.Checksums.GetValueOrDefault(t) != after.Checksums.GetValueOrDefault(t))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>A marker found in a text (case-insensitive), or null.</summary>
    public string? FindMarker(string text) => (_search ??= new MarkerSearch(Markers)).Find(text);
}

/// <summary>
/// Finds any of a fixed set of markers in a text, case-insensitively, in one pass
/// (<see cref="System.Buffers.SearchValues{T}"/> of strings: a multi-string search instead of one
/// scan of the text per marker). Same answer as checking every marker with
/// <c>Contains(marker, OrdinalIgnoreCase)</c>: null exactly when no marker occurs; otherwise a
/// marker that occurs.
/// </summary>
public sealed class MarkerSearch
{
    private readonly string[] _markers;
    private readonly System.Buffers.SearchValues<string>? _values;

    public MarkerSearch(IEnumerable<string> markers)
    {
        _markers = markers.Where(m => m.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _values = _markers.Length == 0 ? null : System.Buffers.SearchValues.Create(_markers, StringComparison.OrdinalIgnoreCase);
    }

    public string? Find(string text)
    {
        if (_values is null)
        {
            return null;
        }
        var at = text.AsSpan().IndexOfAny(_values);
        if (at < 0)
        {
            return null;
        }
        string? found = null;
        foreach (var marker in _markers)
        {
            if (text.AsSpan(at).StartsWith(marker, StringComparison.OrdinalIgnoreCase) && (found is null || marker.Length > found.Length))
            {
                found = marker;
            }
        }
        return found ?? _markers.First(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
    }
}
