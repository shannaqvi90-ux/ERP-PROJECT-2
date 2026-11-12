using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.CustomFields;
using Erp.Kernel.Modules;
using Erp.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Gates.Tests.Rules;

/// <summary>
/// Tenant-defined custom fields (p09) reach a kind of record only through what its module registers,
/// and the registration must be complete or the fields silently do not work: a kind of record that
/// can have custom fields (<c>ModuleBuilder.CustomFieldRecord</c>) must (1) name a registered list that
/// the same permission reads, so the fields' columns, filters and search are served under the record's
/// own permission; (2) have a DbContext whose model carries the value-matching functions
/// (<c>UseCustomFields</c>), or a filter on a field fails on the first request; (3) be a registered kind
/// of audited record, so the history of a value is shown to whoever may read the record; (4) point
/// at a table that exists with a row-level security policy. The database side: the module's tables
/// carry the tenant policy forced and the audit trigger, its functions run as the caller (never
/// security definer), the match functions are stable and the history trigger pins its search path.
/// Everything the module reads of another's records goes through the registration (the boundary gate).
/// </summary>
public sealed class CustomFieldGateTests(GateFixture fixture)
{
    [Fact]
    public async Task Every_kind_of_record_with_custom_fields_is_wired_to_its_list_model_and_audit_trail()
    {
        var catalog = fixture.Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var problems = new List<string>();
        var checks = 0;
        await using var scope = fixture.Env.Factory.Services.CreateAsyncScope();
        await using var admin = await fixture.Env.OpenAdminAsync();
        foreach (var record in catalog.CustomFieldRecordTypes)
        {
            checks++;
            var module = catalog.Modules.Single(m => m.CustomFieldRecordTypes.Contains(record));
            var list = catalog.FindList(record.ListKey);
            if (list is null)
            {
                problems.Add($"{record.Table}: its list {record.ListKey} is not registered");
            }
            else if (list.Permission != record.ReadPermission)
            {
                problems.Add($"{record.Table}: the list {record.ListKey} is read with {list.Permission}, the record kind names {record.ReadPermission}: the fields would be served under another permission");
            }
            if (!module.ListBindings.ContainsKey(record.ListKey))
            {
                problems.Add($"{record.Table}: its list {record.ListKey} has no query binding in module {module.Name}");
            }
            var owns = module.DbContexts.Select(t => (DbContext)scope.ServiceProvider.GetRequiredService(t))
                .Any(db => db.Model.FindEntityType(typeof(CustomFieldMatch)) is not null && db.Model.GetDbFunctions().Any(f => f.Name == "match_text"));
            if (!owns)
            {
                problems.Add($"{record.Table}: no DbContext of module {module.Name} calls modelBuilder.UseCustomFields(): a filter on a custom field would fail");
            }
            if (catalog.FindAuditRecordType(record.Table) is not { } audited)
            {
                problems.Add($"{record.Table}: not a registered kind of audited record: a custom field value's history would show to no one");
            }
            else if (audited.ReadPermission != record.ReadPermission)
            {
                problems.Add($"{record.Table}: its history is read with {audited.ReadPermission} but the record with {record.ReadPermission}");
            }
            await using var exists = new NpgsqlCommand("SELECT relrowsecurity AND relforcerowsecurity FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname || '.' || c.relname = @t", admin);
            exists.Parameters.AddWithValue("t", record.Table);
            if (await exists.ExecuteScalarAsync() is not true)
            {
                problems.Add($"{record.Table}: the table does not exist or its row-level security is not enabled and forced");
            }
        }

        // The module's own objects in the database.
        await using (var tables = new NpgsqlCommand("""
            SELECT c.relname, c.relrowsecurity, c.relforcerowsecurity,
                   EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid AND p.polname = 'tenant_isolation'),
                   EXISTS (SELECT 1 FROM pg_trigger t JOIN pg_proc f ON f.oid = t.tgfoid WHERE t.tgrelid = c.oid AND f.proname = 'capture' AND NOT t.tgisinternal)
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = 'customfields' AND c.relkind = 'r' AND c.relname <> '__ef_migrations_history'
            """, admin))
        await using (var reader = await tables.ExecuteReaderAsync())
        {
            var found = 0;
            while (await reader.ReadAsync())
            {
                found++;
                checks++;
                if (!reader.GetBoolean(1) || !reader.GetBoolean(2) || !reader.GetBoolean(3) || !reader.GetBoolean(4))
                {
                    problems.Add($"customfields.{reader.GetString(0)}: needs row-level security enabled and forced, the tenant policy and the audit trigger");
                }
            }
            if (found < 2)
            {
                problems.Add($"customfields: {found} tables found, expected the definitions and the values");
            }
        }
        await using (var functions = new NpgsqlCommand("""
            SELECT p.proname, p.prosecdef, p.provolatile, COALESCE(array_to_string(p.proconfig, ','), '')
              FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE n.nspname = 'customfields'
            """, admin))
        await using (var reader = await functions.ExecuteReaderAsync())
        {
            var found = 0;
            while (await reader.ReadAsync())
            {
                found++;
                checks++;
                var name = reader.GetString(0);
                if (reader.GetBoolean(1))
                {
                    problems.Add($"customfields.{name}: security definer would read every workspace's values; custom field functions run as the caller");
                }
                // The match, search and value functions are read by lists: STABLE so the planner can turn them into a join
                // or an inlined subquery (a value function is what a list sorts and groups by).
                if (name.StartsWith("match_", StringComparison.Ordinal) || name.StartsWith("value_", StringComparison.Ordinal) || name == "search_text")
                {
                    if (reader.GetChar(2) != 's')
                    {
                        problems.Add($"customfields.{name}: a match or value function must be STABLE so the planner can turn it into a join");
                    }
                }
                else if (!reader.GetString(3).Contains("search_path", StringComparison.Ordinal))
                {
                    problems.Add($"customfields.{name}: a trigger function pins its search path");
                }
            }
            if (found < 12)
            {
                problems.Add($"customfields: {found} functions found, expected the seven match functions, the four value functions and the history trigger");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checks >= Ratchet.Min("rules.customFieldChecks"), $"{checks} custom field checks; ratchet minimum {Ratchet.Min("rules.customFieldChecks")}");
    }
}
