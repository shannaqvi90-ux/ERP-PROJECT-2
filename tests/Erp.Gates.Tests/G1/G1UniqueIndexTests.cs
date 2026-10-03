using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, database layer: uniqueness is per tenant. A unique index or constraint on a tenant table
/// that leaves out <c>tenant_id</c> makes one tenant's rows decide what another tenant may store,
/// and the refusal tells the other tenant what exists: with e-mail unique across the platform,
/// creating a user answers 409 for an address tenant B uses and 201 for an unused one (critic p03
/// round 1, plant U), an oracle the HTTP attack only met by chance. Every unique index on a tenant
/// table must have <c>tenant_id</c> among its key columns, except the single-column primary key on
/// the server-generated <c>id</c> and the reviewed entries in tests/Gates/global-unique-indexes.txt
/// (each with the reason uniqueness across the platform is needed and leaks nothing).
/// </summary>
public sealed class G1UniqueIndexTests(GateFixture fixture)
{
    public const string ReviewedFile = "tests/Gates/global-unique-indexes.txt";

    [Fact]
    public async Task Unique_indexes_on_tenant_tables_include_the_tenant()
    {
        var (problems, checkedIndexes) = await ProblemsAsync(fixture.Env);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checkedIndexes >= Ratchet.Min("g1.uniqueIndexesChecked"),
            $"{checkedIndexes} unique indexes on tenant tables checked; ratchet minimum {Ratchet.Min("g1.uniqueIndexesChecked")}");
    }

    /// <summary>Unique indexes on tenant tables without the tenant among their keys (also run by the
    /// gate self-tests against a planted global index), and how many unique indexes were checked.</summary>
    public static async Task<(List<string> Problems, int Checked)> ProblemsAsync(ErpTestEnvironment env)
    {
        await using var admin = await env.OpenAdminAsync();
        var reviewed = Repo.ReadReviewedList(ReviewedFile);
        var problems = new List<string>();
        problems.AddRange(reviewed.Where(r => string.IsNullOrWhiteSpace(r.Reason)).Select(r => $"{ReviewedFile}: '{r.Entry}' needs a reason after '#'"));
        var allowed = reviewed.Select(r => r.Entry).ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var checkedIndexes = 0;
        foreach (var table in await DbCatalog.TenantTablesAsync(admin))
        {
            var indexes = await DbCatalog.ReadAsync(admin, """
                SELECT i.relname,
                       ix.indisprimary,
                       ix.indnkeyatts,
                       ARRAY(SELECT coalesce(a.attname, '(expression)')
                               FROM unnest(ix.indkey[0:ix.indnkeyatts - 1]) WITH ORDINALITY k(attnum, ord)
                               LEFT JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = k.attnum
                              ORDER BY k.ord) AS keys,
                       pg_get_indexdef(ix.indexrelid)
                  FROM pg_index ix
                  JOIN pg_class i ON i.oid = ix.indexrelid
                  JOIN pg_class c ON c.oid = ix.indrelid
                  JOIN pg_namespace n ON n.oid = c.relnamespace
                 WHERE ix.indisunique AND n.nspname = @s AND c.relname = @t
                 ORDER BY i.relname
                """, r => (Name: r.GetString(0), Primary: r.GetBoolean(1), Keys: r.GetFieldValue<string[]>(3), Definition: r.GetString(4)),
                ("s", table.Schema), ("t", table.Name));
            foreach (var index in indexes)
            {
                checkedIndexes++;
                var entry = $"{table.Qualified}.{index.Name}";
                if (index.Keys.Contains("tenant_id"))
                {
                    continue;
                }
                if (index.Primary && index.Keys is ["id"])
                {
                    continue;
                }
                if (allowed.Contains(entry))
                {
                    used.Add(entry);
                    continue;
                }
                problems.Add($"{entry} is unique across every tenant ({index.Definition}): put tenant_id first among its columns, " +
                             $"or review it in {ReviewedFile} with the reason a value must be unique across the platform");
            }
        }
        problems.AddRange(allowed.Where(a => !used.Contains(a)).Select(a => $"{ReviewedFile}: '{a}' is no longer a unique index without the tenant; remove the entry"));
        return (problems, checkedIndexes);
    }
}
