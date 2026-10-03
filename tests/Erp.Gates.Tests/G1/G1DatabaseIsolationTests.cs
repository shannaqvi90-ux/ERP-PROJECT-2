using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Data;
using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, database layer. Every tenant table carries tenant_id, has row-level security enabled and
/// forced with the standard policy, and the application role cannot see, change, insert into or
/// move rows of another tenant, cannot switch row-level security off and cannot become a role
/// that could.
/// </summary>
public sealed class G1DatabaseIsolationTests(GateFixture fixture)
{
    private ErpTestEnvironment Env => fixture.Env;

    private const string StandardPolicy = "(tenant_id = erp.current_tenant_id())";

    [Fact]
    public async Task Every_table_is_tenant_owned_or_a_reviewed_platform_table()
    {
        await using var admin = await Env.OpenAdminAsync();
        var all = await DbCatalog.AllTablesAsync(admin);
        var tenant = (await DbCatalog.TenantTablesAsync(admin)).ToHashSet();
        var platform = Repo.ReadReviewedList("tests/Gates/platform-tables.txt");
        Assert.All(platform, p => Assert.False(string.IsNullOrWhiteSpace(p.Reason), $"platform-tables.txt entry {p.Entry} needs a reason"));

        bool IsPlatform(TableRef t) => platform.Any(p => Matches(p.Entry, t.Qualified));
        var unexplained = all.Where(t => !tenant.Contains(t) && !IsPlatform(t)).ToList();
        Assert.True(unexplained.Count == 0, "Tables without tenant_id that are not reviewed platform tables: " + string.Join(", ", unexplained));

        var platformWithTenant = all.Where(t => tenant.Contains(t) && IsPlatform(t)).ToList();
        Assert.True(platformWithTenant.Count == 0, "Tables listed as platform tables but owned by tenants: " + string.Join(", ", platformWithTenant));

        Assert.True(tenant.Count >= Ratchet.Min("g1.tenantTables"),
            $"Only {tenant.Count} tenant tables found; ratchet minimum is {Ratchet.Min("g1.tenantTables")}.");
    }

    [Fact]
    public async Task Tenant_tables_enforce_row_level_security_with_the_standard_policy()
    {
        var (problems, tables) = await RowLevelSecurityProblemsAsync(Env);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(tables >= Ratchet.Min("g1.tenantTables"));
    }

    /// <summary>Structural row-level security check of every tenant table (also run by the gate
    /// self-tests against a planted unprotected table).</summary>
    public static async Task<(List<string> Problems, int Tables)> RowLevelSecurityProblemsAsync(ErpTestEnvironment env)
    {
        await using var admin = await env.OpenAdminAsync();
        var tables = await DbCatalog.TenantTablesAsync(admin);
        var reviewed = Repo.ReadReviewedList("tests/Gates/policy-allowlist.txt");
        var problems = new List<string>();
        foreach (var table in tables)
        {
            var columns = await DbCatalog.ColumnsAsync(admin, table);
            var tenantColumn = columns.Single(c => c.Name == "tenant_id");
            if (tenantColumn.Type != "uuid" || !tenantColumn.NotNull)
            {
                problems.Add($"{table}: tenant_id must be uuid NOT NULL (is {tenantColumn.Type}, not null = {tenantColumn.NotNull})");
            }
            if (tenantColumn.Generated)
            {
                problems.Add($"{table}: tenant_id must be a stored value, not generated");
            }

            var flags = await DbCatalog.ReadAsync(admin,
                "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid = to_regclass(@t)",
                r => (Enabled: r.GetBoolean(0), Forced: r.GetBoolean(1)), ("t", table.Qualified));
            if (!flags[0].Enabled) problems.Add($"{table}: row-level security is not enabled");
            if (!flags[0].Forced) problems.Add($"{table}: row-level security is not forced (the owner would bypass it)");

            var policies = await DbCatalog.ReadAsync(admin, """
                SELECT p.polname, p.polcmd::text, p.polpermissive,
                       ARRAY(SELECT CASE WHEN r = 0 THEN 'public' ELSE pg_get_userbyid(r) END FROM unnest(p.polroles) r),
                       pg_get_expr(p.polqual, p.polrelid), pg_get_expr(p.polwithcheck, p.polrelid)
                  FROM pg_policy p WHERE p.polrelid = to_regclass(@t)
                """, r => (Name: r.GetString(0), Command: r.GetString(1), Permissive: r.GetBoolean(2),
                    Roles: r.GetFieldValue<string[]>(3), Using: r.IsDBNull(4) ? null : r.GetString(4), Check: r.IsDBNull(5) ? null : r.GetString(5)),
                ("t", table.Qualified));

            var standard = policies.Where(p => p.Name == "tenant_isolation").ToList();
            if (standard.Count != 1)
            {
                problems.Add($"{table}: needs exactly one tenant_isolation policy");
            }
            else
            {
                var p = standard[0];
                if (p.Command != "*" || !p.Permissive || p.Roles is not ["public"] || p.Using != StandardPolicy || p.Check != StandardPolicy)
                {
                    problems.Add($"{table}: tenant_isolation must be PERMISSIVE FOR ALL TO PUBLIC USING and WITH CHECK {StandardPolicy} " +
                                 $"(is {p.Command} {p.Permissive} [{string.Join(",", p.Roles)}] {p.Using} / {p.Check})");
                }
            }
            // A RESTRICTIVE policy can only narrow what tenant_isolation allows, never widen it; the
            // company_scope policy (rows of the session's companies only) is checked exactly by
            // G1CompanyScopeTests. Any other policy, and a permissive one by that name, is checked here.
            foreach (var other in policies.Where(p => p.Name != "tenant_isolation" && !(p.Name == "company_scope" && !p.Permissive)))
            {
                var key = $"{table.Qualified} {other.Name} {other.Command} {string.Join(",", other.Roles)}";
                if (!reviewed.Any(r => r.Entry == key))
                {
                    problems.Add($"{table}: unreviewed policy '{key}' (add to tests/Gates/policy-allowlist.txt with a reason, only for read-only lookups by {DatabaseRoles.AuthResolver})");
                }
                if (other.Command != "r" || other.Roles is not [DatabaseRoles.AuthResolver])
                {
                    problems.Add($"{table}: extra policy {other.Name} may only grant SELECT to {DatabaseRoles.AuthResolver}");
                }
            }
        }
        return (problems, tables.Count);
    }

    [Fact]
    public async Task Application_role_cannot_bypass_row_level_security_or_become_a_role_that_can()
    {
        await using var admin = await Env.OpenAdminAsync();
        var app = await DbCatalog.ReadAsync(admin,
            "SELECT rolsuper, rolbypassrls, rolcreaterole, rolcreatedb, rolinherit, rolreplication FROM pg_roles WHERE rolname = @r",
            r => (Super: r.GetBoolean(0), Bypass: r.GetBoolean(1), CreateRole: r.GetBoolean(2), CreateDb: r.GetBoolean(3), Inherit: r.GetBoolean(4), Replication: r.GetBoolean(5)),
            ("r", DatabaseRoles.App));
        Assert.Single(app);
        Assert.False(app[0].Super, "erp_app must not be superuser");
        Assert.False(app[0].Bypass, "erp_app must not have BYPASSRLS");
        Assert.False(app[0].CreateRole);
        Assert.False(app[0].CreateDb);
        Assert.False(app[0].Replication);

        var memberships = await DbCatalog.ScalarAsync<long>(admin,
            "SELECT count(*) FROM pg_auth_members m JOIN pg_roles r ON r.oid = m.member WHERE r.rolname = @r", ("r", DatabaseRoles.App));
        Assert.True(memberships == 0, "erp_app must not be a member of any role");

        var owned = await DbCatalog.ScalarAsync<long>(admin, """
            SELECT (SELECT count(*) FROM pg_class WHERE relowner = r.oid) + (SELECT count(*) FROM pg_namespace WHERE nspowner = r.oid)
                 + (SELECT count(*) FROM pg_proc WHERE proowner = r.oid) + (SELECT count(*) FROM pg_type WHERE typowner = r.oid)
                 + (SELECT count(*) FROM pg_database WHERE datdba = r.oid)
              FROM pg_roles r WHERE r.rolname = @r
            """, ("r", DatabaseRoles.App));
        Assert.True(owned == 0, "erp_app must own no table, schema, function, type or database");

        var creatable = await DbCatalog.ReadAsync(admin,
            $"SELECT n.nspname FROM pg_namespace n WHERE {DbCatalog.UserSchemaFilter} AND has_schema_privilege(@r, n.oid, 'CREATE')",
            r => r.GetString(0), ("r", DatabaseRoles.App));
        Assert.True(creatable.Count == 0, "erp_app can create objects in: " + string.Join(", ", creatable));
        Assert.False(await DbCatalog.ScalarAsync<bool>(admin, "SELECT has_database_privilege(@r, current_database(), 'CREATE')", ("r", DatabaseRoles.App)));

        var bypassers = await DbCatalog.ReadAsync(admin, "SELECT rolname FROM pg_roles WHERE rolbypassrls AND NOT rolsuper", r => r.GetString(0));
        Assert.True(bypassers.Count == 0, "Roles with BYPASSRLS: " + string.Join(", ", bypassers));

        var owner = await DbCatalog.ReadAsync(admin, "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = @r",
            r => (r.GetBoolean(0), r.GetBoolean(1)), ("r", DatabaseRoles.Owner));
        Assert.Equal((false, false), owner.Single());

        await using var connection = await Env.OpenAppAsync();
        foreach (var role in new[] { DatabaseRoles.Owner, DatabaseRoles.AuthResolver, "postgres" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => DbCatalog.ExecuteAsync(connection, $"SET ROLE {role}"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
    }

    [Fact]
    public async Task Security_definer_functions_are_exactly_the_reviewed_ones()
    {
        await using var admin = await Env.OpenAdminAsync();
        var functions = await DbCatalog.ReadAsync(admin, $"""
            SELECT n.nspname || '.' || p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ')',
                   pg_get_userbyid(p.proowner), coalesce(array_to_string(p.proconfig, ','), '')
              FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE p.prosecdef AND {DbCatalog.UserSchemaFilter}
             ORDER BY 1
            """, r => (Signature: r.GetString(0), Owner: r.GetString(1), Config: r.GetString(2)));
        var reviewed = Repo.ReadReviewedList("tests/Gates/security-definer-allowlist.txt");
        Assert.All(reviewed, r => Assert.False(string.IsNullOrWhiteSpace(r.Reason), $"{r.Entry} needs a reason"));
        Assert.Equal(reviewed.Select(r => r.Entry).Order(StringComparer.Ordinal), functions.Select(f => f.Signature).Order(StringComparer.Ordinal));
        foreach (var function in functions)
        {
            Assert.True(function.Owner == DatabaseRoles.AuthResolver, $"{function.Signature} must be owned by {DatabaseRoles.AuthResolver}, not {function.Owner}");
            Assert.True(function.Config.Contains("search_path=", StringComparison.Ordinal), $"{function.Signature} must pin search_path");
        }
        Assert.True(functions.Count <= Ratchet.Max("g1.securityDefinerFunctions"),
            $"{functions.Count} security-definer functions; ratchet maximum {Ratchet.Max("g1.securityDefinerFunctions")}");
    }

    [Fact]
    public async Task Foreign_keys_between_tenant_tables_include_the_tenant()
    {
        await using var admin = await Env.OpenAdminAsync();
        var problems = await DbCatalog.ReadAsync(admin, $"""
            SELECT c.conname || ' on ' || cn.nspname || '.' || cl.relname
              FROM pg_constraint c
              JOIN pg_class cl ON cl.oid = c.conrelid JOIN pg_namespace cn ON cn.oid = cl.relnamespace
              JOIN pg_namespace n ON n.oid = cn.oid
             WHERE c.contype = 'f' AND {DbCatalog.UserSchemaFilter}
               AND EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.conrelid AND a.attname = 'tenant_id')
               AND EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.confrelid AND a.attname = 'tenant_id')
               AND NOT EXISTS (
                   SELECT 1 FROM unnest(c.conkey, c.confkey) AS k(child, parent)
                     JOIN pg_attribute ca ON ca.attrelid = c.conrelid AND ca.attnum = k.child
                     JOIN pg_attribute pa ON pa.attrelid = c.confrelid AND pa.attnum = k.parent
                    WHERE ca.attname = 'tenant_id' AND pa.attname = 'tenant_id')
            """, r => r.GetString(0));
        Assert.True(problems.Count == 0, "Foreign keys that could reference another tenant's row: " + string.Join(", ", problems));
    }

    [Fact]
    public async Task Tenant_tables_have_an_id_primary_key()
    {
        await using var admin = await Env.OpenAdminAsync();
        var problems = new List<string>();
        foreach (var table in await DbCatalog.TenantTablesAsync(admin))
        {
            var pk = await DbCatalog.ReadAsync(admin, """
                SELECT array_agg(a.attname ORDER BY a.attnum)
                  FROM pg_constraint c JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)
                 WHERE c.conrelid = to_regclass(@t) AND c.contype = 'p'
                """, r => r.IsDBNull(0) ? [] : r.GetFieldValue<string[]>(0), ("t", table.Qualified));
            if (pk.Single() is not ["id"])
            {
                problems.Add($"{table}: primary key must be (id)");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task Application_role_sees_and_changes_only_its_own_tenant_in_every_tenant_table()
    {
        var a = Env.TenantA.Id;
        var b = Env.TenantB.Id;
        await using var admin = await Env.OpenAdminAsync();
        var tables = await DbCatalog.TenantTablesAsync(admin);
        var problems = new List<string>();
        var checkedTables = 0;

        foreach (var table in tables)
        {
            var name = table.Qualified;
            var countA = await DbCatalog.ScalarAsync<long>(admin, $"SELECT count(*) FROM {name} WHERE tenant_id = @a", ("a", a));
            var countB = await DbCatalog.ScalarAsync<long>(admin, $"SELECT count(*) FROM {name} WHERE tenant_id = @b", ("b", b));
            if (countA == 0 || countB == 0)
            {
                problems.Add($"{name}: gate fixture must hold rows of both tenants (A {countA}, B {countB}); seed canary rows for it");
                continue;
            }
            var columns = await DbCatalog.ColumnsAsync(admin, table);
            var insertable = columns.Where(c => !c.Identity && !c.Generated).Select(c => c.Name).ToList();
            var privileges = await DbCatalog.ReadAsync(admin,
                "SELECT has_table_privilege(@r, to_regclass(@t), 'UPDATE'), has_table_privilege(@r, to_regclass(@t), 'DELETE')",
                r => (Update: r.GetBoolean(0), Delete: r.GetBoolean(1)), ("r", DatabaseRoles.App), ("t", name));

            await using var app = await Env.OpenAppAsync();

            // No tenant bound: nothing is visible.
            var unbound = await DbCatalog.ScalarAsync<long>(app, $"SELECT count(*) FROM {name}");
            if (unbound != 0) problems.Add($"{name}: {unbound} rows visible without a tenant");

            await using (var tx = await app.BeginTransactionAsync())
            {
                await BindAsync(app, tx, a);
                var visible = await DbCatalog.ScalarAsync<long>(app, $"SELECT count(*) FROM {name}");
                var foreign = await DbCatalog.ScalarAsync<long>(app, $"SELECT count(*) FROM {name} WHERE tenant_id <> @a", ("a", a));
                if (visible != countA) problems.Add($"{name}: tenant A sees {visible} rows, owns {countA}");
                if (foreign != 0) problems.Add($"{name}: tenant A sees {foreign} rows of other tenants");

                if (privileges[0].Update)
                {
                    var touched = await ExecAsync(app, tx, $"UPDATE {name} SET tenant_id = tenant_id WHERE tenant_id = '{b}'");
                    if (touched != 0) problems.Add($"{name}: tenant A updated {touched} rows of tenant B");
                }
                if (privileges[0].Delete)
                {
                    var deleted = await ExecAsync(app, tx, $"DELETE FROM {name} WHERE tenant_id = '{b}'");
                    if (deleted != 0) problems.Add($"{name}: tenant A deleted {deleted} rows of tenant B");
                }

                await ExpectDenied(app, tx, problems, $"{name}: moving an A row to tenant B",
                    $"UPDATE {name} SET tenant_id = '{b}' WHERE id = (SELECT id FROM {name} LIMIT 1)");

                var list = string.Join(", ", insertable);
                var overrides = insertable.Contains("id")
                    ? $"jsonb_build_object('id', gen_random_uuid(), 'tenant_id', '{b}'::uuid)"
                    : $"jsonb_build_object('tenant_id', '{b}'::uuid)";
                await ExpectDenied(app, tx, problems, $"{name}: inserting a row for tenant B",
                    $"INSERT INTO {name} ({list}) SELECT {string.Join(", ", insertable.Select(c => "r." + c))} " +
                    $"FROM {name} x, LATERAL jsonb_populate_record(NULL::{name}, to_jsonb(x) || {overrides}) r LIMIT 1");

                await ExpectDenied(app, tx, problems, $"{name}: reading with row_security off",
                    $"SET LOCAL row_security = off; SELECT count(*) FROM {name}");

                await tx.RollbackAsync();
            }

            await using (var tx = await app.BeginTransactionAsync())
            {
                await ExpectDenied(app, tx, problems, $"{name}: disabling row-level security",
                    $"ALTER TABLE {name} DISABLE ROW LEVEL SECURITY");
                await tx.RollbackAsync();
            }
            await using (var tx = await app.BeginTransactionAsync())
            {
                await ExpectDenied(app, tx, problems, $"{name}: dropping the policy",
                    $"DROP POLICY tenant_isolation ON {name}");
                await tx.RollbackAsync();
            }
            checkedTables++;
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checkedTables >= Ratchet.Min("g1.tablesAttackedInDatabase"),
            $"{checkedTables} tables attacked; ratchet minimum is {Ratchet.Min("g1.tablesAttackedInDatabase")}");
    }

    [Fact]
    public async Task A_tenant_set_for_the_whole_connection_or_left_by_an_earlier_transaction_is_ignored()
    {
        var a = Env.TenantA.Id;
        await using var app = await Env.OpenAppAsync();

        // Session-level settings (not transaction-local), as a careless or hostile statement could
        // leave on a pooled connection.
        await DbCatalog.ExecuteAsync(app,
            $"SELECT set_config('app.tenant_id', '{a}', false), set_config('app.tenant_tx', extract(epoch from now())::text, false)");
        Assert.Equal(0L, await DbCatalog.ScalarAsync<long>(app, "SELECT count(*) FROM identity.users"));
        Assert.Equal(0L, await DbCatalog.ScalarAsync<long>(app, "SELECT count(*) FROM identity.roles"));

        // A transaction-local tenant from an earlier transaction is gone; a fresh bind works.
        await using (var tx = await app.BeginTransactionAsync())
        {
            await BindAsync(app, tx, a);
            await using var count = new NpgsqlCommand("SELECT count(*) FROM identity.users", app, tx);
            Assert.True((long)(await count.ExecuteScalarAsync())! > 0);
            await tx.CommitAsync();
        }
        await using (var tx = await app.BeginTransactionAsync())
        {
            await using var count = new NpgsqlCommand("SELECT count(*) FROM identity.users", app, tx);
            Assert.Equal(0L, await count.ExecuteScalarAsync());
            await tx.RollbackAsync();
        }

        // The tenant id alone, without the transaction marker, binds nothing.
        await using (var tx = await app.BeginTransactionAsync())
        {
            await using var bind = new NpgsqlCommand($"SELECT set_config('app.tenant_id', '{a}', true)", app, tx);
            await bind.ExecuteNonQueryAsync();
            await using var count = new NpgsqlCommand("SELECT count(*) FROM identity.users", app, tx);
            Assert.Equal(0L, await count.ExecuteScalarAsync());
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task Audit_trail_is_append_only_for_the_application()
    {
        await using var app = await Env.OpenAppAsync();
        await using var tx = await app.BeginTransactionAsync();
        await BindAsync(app, tx, Env.TenantA.Id);
        var problems = new List<string>();
        await ExpectDenied(app, tx, problems, "updating audit entries", "UPDATE audit.entries SET action = 'update'");
        await ExpectDenied(app, tx, problems, "deleting audit entries", "DELETE FROM audit.entries");
        await ExpectDenied(app, tx, problems, "truncating audit entries", "TRUNCATE audit.entries");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Bind a transaction to a tenant the way the platform does for system work: the
    /// tenant and the transaction it belongs to, both transaction-local, with every company of the
    /// tenant in scope (company scoping, the layer within a tenant, has its own gate:
    /// <see cref="G1CompanyScopeTests"/>).</summary>
    internal static async Task BindAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenant)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @t, true), set_config('app.tenant_tx', extract(epoch from now())::text, true), " +
            "set_config('app.company_scope', 'all', true), set_config('app.company_tx', extract(epoch from now())::text, true)", connection, tx);
        command.Parameters.AddWithValue("t", tenant.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ExecAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>Run the statement in a savepoint; it must fail with insufficient privilege
    /// (row-level security violation or missing grant).</summary>
    private static async Task ExpectDenied(NpgsqlConnection connection, NpgsqlTransaction tx, List<string> problems, string what, string sql)
    {
        await tx.SaveAsync("attempt");
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            await command.ExecuteNonQueryAsync();
            problems.Add($"{what}: succeeded");
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            // Denied as required.
        }
        catch (PostgresException error)
        {
            problems.Add($"{what}: failed with {error.SqlState} {error.MessageText} instead of insufficient privilege");
        }
        await tx.RollbackAsync("attempt");
    }

    private static bool Matches(string pattern, string value) =>
        pattern.StartsWith("*.", StringComparison.Ordinal) ? value.EndsWith(pattern[1..], StringComparison.Ordinal) : pattern == value;
}
