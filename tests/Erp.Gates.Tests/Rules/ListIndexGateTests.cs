using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.Rules;

/// <summary>
/// Every list the database serves is backed by proper indexes, read from PostgreSQL's catalogue:
/// each search field by a GIN trigram index (<c>gin_trgm_ops</c>, so ILIKE '%word%' does not scan
/// the table), and each sortable column by a B-tree index leading with (tenant_id, column), so a
/// sorted page and a keyset step seek instead of sorting the tenant's rows. A column served from
/// a computed value cannot be indexed and is refused. Lists the endpoint filters in memory must
/// say why (small, bounded lists).
/// </summary>
public sealed class ListIndexGateTests(GateFixture fixture)
{
    [Fact]
    public async Task Search_fields_and_sortable_columns_of_database_lists_are_indexed()
    {
        var catalog = fixture.Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        await using var scope = fixture.Env.Factory.Services.CreateAsyncScope();
        var models = catalog.DbContexts.Select(t => ((DbContext)scope.ServiceProvider.GetRequiredService(t)).Model).ToList();
        await using var admin = await fixture.Env.OpenAdminAsync();
        var problems = new List<string>();
        var checkedIndexes = 0;
        var databaseLists = 0;
        foreach (var binding in catalog.ListBindings)
        {
            var list = binding.Definition;
            if (binding.InMemoryReason is { } reason)
            {
                if (reason.Trim().Length < 20)
                {
                    problems.Add($"list '{list.Key}': is filtered in memory without a reviewable reason");
                }
                continue;
            }
            databaseLists++;
            var entity = models.Select(m => m.FindEntityType(binding.RowType)).FirstOrDefault(e => e is not null);
            if (entity is null)
            {
                problems.Add($"list '{list.Key}': {binding.RowType.Name} is not an entity of any module DbContext; bind the list to a table or declare it in memory");
                continue;
            }
            var table = entity.GetTableName()!;
            var schema = entity.GetSchema() ?? entity.Model.GetDefaultSchema() ?? "public";
            var store = StoreObjectIdentifier.Table(table, schema);
            string? ColumnOf(string key)
            {
                var bound = binding.Columns.SingleOrDefault(c => c.Key == key);
                return bound?.Member is { } member ? entity.FindProperty(member)?.GetColumnName(store) : null;
            }

            foreach (var field in list.SearchFields)
            {
                checkedIndexes++;
                if (ColumnOf(field) is not { } column)
                {
                    problems.Add($"list '{list.Key}': search field '{field}' reads a computed value, which no index can serve");
                    continue;
                }
                var trigram = await DbCatalog.ScalarAsync<long>(admin, """
                    SELECT count(*)
                      FROM pg_index i
                      JOIN pg_class t ON t.oid = i.indrelid JOIN pg_namespace n ON n.oid = t.relnamespace
                      JOIN pg_class x ON x.oid = i.indexrelid JOIN pg_am am ON am.oid = x.relam
                      JOIN LATERAL unnest(i.indkey::int2[], i.indclass::oid[]) AS k(attnum, opclass) ON true
                      JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
                      JOIN pg_opclass oc ON oc.oid = k.opclass
                     WHERE n.nspname = @s AND t.relname = @t AND a.attname = @c AND am.amname = 'gin' AND oc.opcname = 'gin_trgm_ops'
                       AND i.indisvalid AND i.indpred IS NULL
                    """, ("s", schema), ("t", table), ("c", column));
                if (trigram == 0)
                {
                    problems.Add($"list '{list.Key}': search field '{field}' ({schema}.{table}.{column}) has no GIN trigram index (gin_trgm_ops)");
                }
            }
            foreach (var sortable in list.Columns.Where(c => c.Sortable))
            {
                checkedIndexes++;
                if (ColumnOf(sortable.Key) is not { } column)
                {
                    problems.Add($"list '{list.Key}': sortable column '{sortable.Key}' reads a computed value, which no index can serve");
                    continue;
                }
                var btree = await DbCatalog.ScalarAsync<long>(admin, """
                    SELECT count(*)
                      FROM pg_index i
                      JOIN pg_class t ON t.oid = i.indrelid JOIN pg_namespace n ON n.oid = t.relnamespace
                      JOIN pg_class x ON x.oid = i.indexrelid JOIN pg_am am ON am.oid = x.relam
                     WHERE n.nspname = @s AND t.relname = @t AND am.amname = 'btree' AND i.indisvalid AND i.indpred IS NULL
                       AND (SELECT attname FROM pg_attribute WHERE attrelid = t.oid AND attnum = i.indkey[0]) = 'tenant_id'
                       AND (SELECT attname FROM pg_attribute WHERE attrelid = t.oid AND attnum = i.indkey[1]) = @c
                    """, ("s", schema), ("t", table), ("c", column));
                if (btree == 0)
                {
                    problems.Add($"list '{list.Key}': sortable column '{sortable.Key}' ({schema}.{table}.{column}) has no B-tree index on (tenant_id, {column}, …)");
                }
            }
        }
        Assert.True(databaseLists > 0, "No list is served by the database; the index gate checked nothing.");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checkedIndexes >= Ratchet.Min("rules.listIndexesChecked"), $"{checkedIndexes} list indexes checked; ratchet minimum {Ratchet.Min("rules.listIndexesChecked")}");
    }
}
