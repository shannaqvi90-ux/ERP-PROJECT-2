using Erp.Gates.Tests.G1;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Data;
using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, the company write permission in the database (critic p02 rounds 12 and 13: a manager of the
/// Dubai LLC, read-only in the free-zone entity, closed a free-zone branch with one set-based
/// <c>ExecuteUpdate</c> that ran before the endpoint's check and past the kernel's SaveChanges
/// guard). A request whose permission is held only in some companies sets a write limit
/// (<c>app.write_scope</c> 'list', <c>app.write_ids</c>, <c>app.write_tx</c>); every company table
/// carries the RESTRICTIVE <c>company_write_insert</c>, <c>company_write_update</c> and
/// <c>company_write_delete</c> policies, so under that limit the application role reads every
/// company of its scope but inserts, changes, moves and deletes rows of the permitted companies
/// alone, whatever statement it sends. Without a limit the company scope alone applies (the
/// control), and a limit left by another transaction permits nothing (fail closed).
/// </summary>
public sealed class G2CompanyWritePolicyTests(GateFixture fixture)
{
    private ErpTestEnvironment Env => fixture.Env;

    private const string Standard = TenantSql.CompanyWriteExpression;

    [Fact]
    public async Task Every_company_table_carries_the_company_write_policies()
    {
        var (problems, tables) = await WritePolicyProblemsAsync(Env);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(tables >= Ratchet.Min("g1.companyTables"), $"{tables} company tables; ratchet minimum {Ratchet.Min("g1.companyTables")}");
    }

    /// <summary>Structural check of every company table (also run by the self-tests).</summary>
    public static async Task<(List<string> Problems, int Tables)> WritePolicyProblemsAsync(ErpTestEnvironment env)
    {
        await using var admin = await env.OpenAdminAsync();
        var problems = new List<string>();
        var tables = await DbCatalog.CompanyTablesAsync(admin);
        foreach (var table in tables)
        {
            var policies = await DbCatalog.ReadAsync(admin, """
                SELECT p.polname, p.polcmd::text, p.polpermissive,
                       ARRAY(SELECT CASE WHEN r = 0 THEN 'public' ELSE pg_get_userbyid(r) END FROM unnest(p.polroles) r),
                       pg_get_expr(p.polqual, p.polrelid), pg_get_expr(p.polwithcheck, p.polrelid)
                  FROM pg_policy p WHERE p.polrelid = to_regclass(@t) AND p.polname LIKE 'company\_write\_%'
                """, r => (Name: r.GetString(0), Command: r.GetString(1), Permissive: r.GetBoolean(2), Roles: r.GetFieldValue<string[]>(3),
                    Using: r.IsDBNull(4) ? null : r.GetString(4), Check: r.IsDBNull(5) ? null : r.GetString(5)),
                ("t", table.Qualified));
            foreach (var (name, command) in TenantSql.CompanyWritePolicies)
            {
                var found = policies.Where(p => p.Name == name).ToList();
                var expectedUsing = command is "w" or "d" ? Standard : null;
                var expectedCheck = command is "a" or "w" ? Standard : null;
                if (found.Count != 1 || found[0].Permissive || found[0].Command != command || found[0].Roles is not ["public"] ||
                    found[0].Using != expectedUsing || found[0].Check != expectedCheck)
                {
                    var what = found.Count == 1 ? $"is {found[0].Command} permissive={found[0].Permissive} [{string.Join(",", found[0].Roles)}] {found[0].Using} / {found[0].Check}" : $"{found.Count} found";
                    problems.Add($"{table}: needs the RESTRICTIVE {name} policy (command {command}, TO PUBLIC, " +
                                 $"USING {expectedUsing ?? "none"}, WITH CHECK {expectedCheck ?? "none"}; migrationBuilder.ProtectCompanyWrites): {what}");
                }
            }
            foreach (var extra in policies.Where(p => TenantSql.CompanyWritePolicies.All(w => w.Name != p.Name)))
            {
                problems.Add($"{table}: unknown company write policy {extra.Name}");
            }
        }
        return (problems, tables.Count);
    }

    [Fact]
    public async Task Under_a_write_limit_the_application_role_writes_only_the_permitted_companies_rows()
    {
        var tenant = Env.TenantA.Id;
        await using var admin = await Env.OpenAdminAsync();
        var companies = await DbCatalog.ReadAsync(admin, "SELECT id FROM tenancy.companies WHERE tenant_id = @t ORDER BY id", r => r.GetGuid(0), ("t", tenant));
        Assert.True(companies.Count >= 2, "the gate fixture needs two companies in tenant A");
        var (x, y) = (companies[0], companies[1]);
        var tables = await DbCatalog.CompanyTablesAsync(admin);
        var problems = new List<string>();
        var actor = Guid.NewGuid();
        await using var app = await Env.OpenAppAsync();
        foreach (var table in tables)
        {
            var name = table.Qualified;
            var inX = await DbCatalog.ScalarAsync<long>(admin, $"SELECT count(*) FROM {name} WHERE tenant_id = @t AND company_id = @c", ("t", tenant), ("c", x));
            var inY = await DbCatalog.ScalarAsync<long>(admin, $"SELECT count(*) FROM {name} WHERE tenant_id = @t AND company_id = @c", ("t", tenant), ("c", y));
            if (inX == 0 || inY == 0)
            {
                problems.Add($"{name}: the gate fixture must hold rows of both companies of tenant A (X {inX}, Y {inY}); seed them");
                continue;
            }
            var columns = await DbCatalog.ColumnsAsync(admin, table);
            var insertable = columns.Where(c => !c.Identity && !c.Generated).Select(c => c.Name).ToList();
            var overrides = $"jsonb_build_object('id', gen_random_uuid(), 'company_id', '{y}'::uuid)";
            var insertIntoY = $"INSERT INTO {name} ({string.Join(", ", insertable)}) SELECT {string.Join(", ", insertable.Select(c => "r." + c))} " +
                              $"FROM {name} x, LATERAL jsonb_populate_record(NULL::{name}, to_jsonb(x) || {overrides}) r WHERE x.company_id = '{x}' LIMIT 1";

            // Both companies in scope, the permission held in X alone.
            await using (var tx = await app.BeginTransactionAsync())
            {
                await G1CompanyScopeTests.BindAsync(app, tx, tenant, actor, scope: "list", companies: [x, y]);
                await LimitAsync(app, tx, [x], marked: true);
                var seenY = await DbCatalog.ScalarAsync<long>(app, $"SELECT count(*) FROM {name} WHERE company_id = '{y}'");
                if (seenY != inY) problems.Add($"{name}: under a write limit to X, {seenY} of Y's {inY} rows readable (reads must not change)");
                var updated = await ExecAsync(app, tx, $"UPDATE {name} SET company_id = company_id WHERE company_id = '{y}'");
                if (updated != 0) problems.Add($"{name}: under a write limit to X, {updated} rows of Y updated");
                var deleted = await ExecAsync(app, tx, $"DELETE FROM {name} WHERE company_id = '{y}'");
                if (deleted != 0) problems.Add($"{name}: under a write limit to X, {deleted} rows of Y deleted");
                await ExpectDeniedAsync(app, tx, problems, $"{name}: under a write limit to X, inserting a row into Y", insertIntoY);
                await ExpectDeniedAsync(app, tx, problems, $"{name}: under a write limit to X, moving a row of X into Y",
                    $"UPDATE {name} SET company_id = '{y}' WHERE id = (SELECT id FROM {name} WHERE company_id = '{x}' LIMIT 1)");
                // The control: X's rows are written as before.
                var own = await ExecAsync(app, tx, $"UPDATE {name} SET company_id = company_id WHERE company_id = '{x}'");
                if (own != inX) problems.Add($"{name}: under a write limit to X, {own} of X's {inX} rows updated (expected all)");
                await tx.RollbackAsync();
            }

            // No limit (a permission held in every company): the company scope alone applies.
            await using (var tx = await app.BeginTransactionAsync())
            {
                await G1CompanyScopeTests.BindAsync(app, tx, tenant, actor, scope: "list", companies: [x, y]);
                await using (var none = new NpgsqlCommand("SELECT set_config('app.write_scope', '', true)", app, tx))
                {
                    await none.ExecuteNonQueryAsync();
                }
                var updated = await ExecAsync(app, tx, $"UPDATE {name} SET company_id = company_id WHERE company_id = '{y}'");
                if (updated != inY) problems.Add($"{name}: without a write limit {updated} of Y's {inY} rows updated, so the refusals above prove nothing");
                await tx.RollbackAsync();
            }

            // A limit whose marker is not this transaction's (left at session level) permits nothing.
            await using (var tx = await app.BeginTransactionAsync())
            {
                await G1CompanyScopeTests.BindAsync(app, tx, tenant, actor, scope: "list", companies: [x, y]);
                await LimitAsync(app, tx, [x, y], marked: false);
                var updated = await ExecAsync(app, tx, $"UPDATE {name} SET company_id = company_id WHERE company_id IN ('{x}', '{y}')");
                if (updated != 0) problems.Add($"{name}: a write limit not marked with this transaction let {updated} rows be updated (expected none: fail closed)");
                await ExpectDeniedAsync(app, tx, problems, $"{name}: a write limit not marked with this transaction, inserting a row", insertIntoY);
                await tx.RollbackAsync();
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(tables.Count >= Ratchet.Min("g1.companyTables"));
    }

    /// <summary>Set the write limit the way <see cref="ErpDbSession.LimitToPermissionAsync"/> does
    /// (or, unmarked, with a marker of no transaction).</summary>
    private static async Task LimitAsync(NpgsqlConnection connection, NpgsqlTransaction tx, IReadOnlyList<Guid> companies, bool marked)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.write_scope', 'list', true), set_config('app.write_ids', @ids, true), " +
            (marked ? "set_config('app.write_tx', extract(epoch from now())::text, true)" : "set_config('app.write_tx', '0', true)"), connection, tx);
        command.Parameters.AddWithValue("ids", string.Join(',', companies));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ExecAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string sql)
    {
        await tx.SaveAsync("attempt");
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            return await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException error)
        {
            await tx.RollbackAsync("attempt");
            throw new InvalidOperationException($"{sql}: {error.SqlState} {error.MessageText}", error);
        }
    }

    private static async Task ExpectDeniedAsync(NpgsqlConnection connection, NpgsqlTransaction tx, List<string> problems, string what, string sql)
    {
        await tx.SaveAsync("denied");
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            var rows = await command.ExecuteNonQueryAsync();
            problems.Add($"{what}: succeeded ({rows} rows)");
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            // Refused by a policy, as required.
        }
        catch (PostgresException error)
        {
            problems.Add($"{what}: failed with {error.SqlState} {error.MessageText} instead of insufficient privilege");
        }
        await tx.RollbackAsync("denied");
    }
}
