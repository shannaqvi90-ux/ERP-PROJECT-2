using Erp.Testing;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// Tenant B's real values, read from the database with the superuser: every id (uuid primary key)
/// and every text value of every tenant table that tenant A does not also hold (e-mails, names,
/// codes, hashes, canary-bearing text). The G1 attack sends each of them, as tenant A, through
/// every documented route, query and body parameter, so an endpoint that looks something up by
/// e-mail, code or name without the tenant is found, not only one that trusts an id.
/// </summary>
public sealed class VictimValues
{
    /// <summary>Text values per column kept, so a large table cannot crowd out the others.</summary>
    private const int PerColumn = 8;

    public required IReadOnlyList<Guid> Ids { get; init; }

    /// <summary>Up to five ids of every tenant table plus the tenant id: what the parameter
    /// attack sends where an id is expected (one id per table is enough to expose a lookup that
    /// ignores the tenant; five allows for rows of different kinds).</summary>
    public required IReadOnlyList<Guid> IdSample { get; init; }

    /// <summary>A small cross-section (tenant id and code, one id per table, one value per text
    /// column) for parameters the app does not document and catch-all routes.</summary>
    public required IReadOnlyList<string> Probe { get; init; }

    /// <summary>B-only text values (never the bare canary).</summary>
    public required IReadOnlyList<string> Strings { get; init; }

    /// <summary>Extra leak markers: B-only text values long enough to be unmistakable (password
    /// hashes, e-mails, names), outside the audit trail whose correlation ids look like trace ids.</summary>
    public required IReadOnlyList<string> Markers { get; init; }

    /// <summary>Sampled ids then every text value.</summary>
    public IEnumerable<string> All => IdSample.Select(i => i.ToString()).Concat(Strings);

    public static async Task<VictimValues> ReadAsync(ErpTestEnvironment env, TenantSnapshot victim, Guid attacker)
    {
        await using var admin = await env.OpenAdminAsync();
        var strings = new List<string>();
        var markers = new List<string>();
        var probe = new List<string> { victim.TenantId.ToString(), victim.Code };
        probe.AddRange(victim.IdsByTable.Values.Where(ids => ids.Count > 0).Select(ids => ids[0].ToString()));
        foreach (var table in await DbCatalog.TenantTablesAsync(admin))
        {
            foreach (var column in await DbCatalog.ColumnsAsync(admin, table))
            {
                if (column.Name == "tenant_id" || !(column.Type.StartsWith("text", StringComparison.Ordinal) ||
                                                    column.Type.StartsWith("character varying", StringComparison.Ordinal) ||
                                                    column.Type == "citext"))
                {
                    continue;
                }
                if (column.Type.EndsWith("[]", StringComparison.Ordinal))
                {
                    continue;
                }
                var name = $"\"{column.Name}\"";
                var values = await DbCatalog.ReadAsync(admin, $"""
                    SELECT DISTINCT v FROM (SELECT {name}::text AS v FROM {table.Qualified} WHERE tenant_id = @b) b
                     WHERE v IS NOT NULL AND length(v) >= 3
                       AND NOT EXISTS (SELECT 1 FROM {table.Qualified} a WHERE a.tenant_id = @a AND a.{name}::text = b.v)
                     ORDER BY v LIMIT {PerColumn}
                    """, r => r.GetString(0), ("b", victim.TenantId), ("a", attacker));
                if (values.FirstOrDefault(v => victim.Canary is null || !string.Equals(v.Trim(), victim.Canary, StringComparison.OrdinalIgnoreCase)) is { } first)
                {
                    probe.Add(first);
                }
                foreach (var value in values)
                {
                    if (victim.Canary is { } canary && string.Equals(value.Trim(), canary, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    strings.Add(value);
                    if (table.Schema != "audit" && value.Length >= 16)
                    {
                        markers.Add(value);
                    }
                }
            }
        }
        strings.Add(victim.Code);
        return new VictimValues
        {
            Ids = victim.AllIds.ToList(),
            IdSample = victim.IdsByTable.Values.SelectMany(ids => ids.Take(5)).Append(victim.TenantId).Distinct().ToList(),
            Probe = probe.Distinct(StringComparer.Ordinal).ToList(),
            Strings = strings.Distinct(StringComparer.Ordinal).ToList(),
            Markers = markers.Distinct(StringComparer.Ordinal).ToList(),
        };
    }
}
