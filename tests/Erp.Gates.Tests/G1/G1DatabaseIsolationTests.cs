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
            // G1CompanyScopeTests. Any other policy, and a permissive one by these names, is checked here.
            // So are the RESTRICTIVE company_scope_update and company_scope_delete policies that keep
            // a user's own rows read-only outside the scope (exact command and expression there).
            foreach (var other in policies.Where(p => p.Name != "tenant_isolation" &&
                                                      !(p.Name is "company_scope" or "company_scope_update" or "company_scope_delete" && !p.Permissive)))
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

    /// <summary>
    /// PostgreSQL's statistics count every tenant's rows together (critic p00 round 8, plant L3: a
    /// list total from pg_stat_user_tables.n_live_tup). The application role can read none of them:
    /// no statistics view (row counts, activity, other sessions, sampled column values), no
    /// pg_stat_get_* function and no size function. Checked from the catalogs for every such object
    /// there is, and by asking as erp_app.
    /// </summary>
    [Fact]
    public async Task Application_role_cannot_read_statistics_that_count_every_tenant()
    {
        await using var admin = await Env.OpenAdminAsync();
        var views = await DbCatalog.ReadAsync(admin, """
            SELECT c.relname, has_table_privilege(@r, c.oid, 'SELECT')
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = 'pg_catalog' AND c.relkind IN ('v', 'm', 'r') AND (c.relname LIKE 'pg\_stat%' OR c.relname LIKE 'pg\_statio%')
            """, r => (Name: r.GetString(0), Readable: r.GetBoolean(1)), ("r", DatabaseRoles.App));
        var functions = await DbCatalog.ReadAsync(admin, $"""
            SELECT p.oid::regprocedure::text, has_function_privilege(@r, p.oid, 'EXECUTE')
              FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE n.nspname = 'pg_catalog' AND (p.proname LIKE 'pg\_stat\_get%' OR p.proname IN ({string.Join(", ", KernelSql.SizeFunctions.Select(f => $"'{f}'"))}))
            """, r => (Name: r.GetString(0), Executable: r.GetBoolean(1)), ("r", DatabaseRoles.App));
        Assert.True(views.Count >= 40, $"only {views.Count} statistics views found: the catalog query is blind");
        Assert.True(functions.Count >= 100, $"only {functions.Count} statistics functions found: the catalog query is blind");
        Assert.Contains(views, v => v.Name == "pg_stat_user_tables");
        Assert.Contains(views, v => v.Name == "pg_stats");
        var open = views.Where(v => v.Readable).Select(v => v.Name).Concat(functions.Where(f => f.Executable).Select(f => f.Name)).ToList();
        Assert.True(open.Count == 0, "erp_app can read statistics that count or sample every tenant's rows: " + string.Join(", ", open));

        // pg_class's row and page estimates count every tenant's rows; its other columns stay
        // readable (client libraries need them).
        var classColumns = await DbCatalog.ReadAsync(admin, """
            SELECT a.attname, has_column_privilege(@r, 'pg_catalog.pg_class'::regclass, a.attnum, 'SELECT')
              FROM pg_attribute a WHERE a.attrelid = 'pg_catalog.pg_class'::regclass AND a.attnum > 0 AND NOT a.attisdropped
            """, r => (Name: r.GetString(0), Readable: r.GetBoolean(1)), ("r", DatabaseRoles.App));
        Assert.True(classColumns.Count >= 30, $"only {classColumns.Count} pg_class columns found: the catalog query is blind");
        var estimates = classColumns.Where(c => KernelSql.PgClassEstimateColumns.Contains(c.Name)).ToList();
        Assert.True(estimates.Count >= 3, "pg_class has none of reltuples, relpages, relallvisible: the check is blind");
        Assert.True(estimates.All(c => !c.Readable), "erp_app can read pg_class estimates: " + string.Join(", ", estimates.Where(c => c.Readable).Select(c => c.Name)));
        var closedOthers = classColumns.Where(c => !KernelSql.PgClassEstimateColumns.Contains(c.Name) && !c.Readable).Select(c => c.Name).ToList();
        Assert.True(closedOthers.Count == 0, "pg_class columns client libraries need are closed to erp_app: " + string.Join(", ", closedOthers));

        await using var app = await Env.OpenAppAsync();
        Assert.Equal("users", await DbCatalog.ScalarAsync<string>(app, "SELECT relname::text FROM pg_class WHERE oid = 'identity.users'::regclass"));
        foreach (var sql in new[]
                 {
                     "SELECT reltuples FROM pg_class WHERE oid = 'identity.users'::regclass",
                     "SELECT relpages FROM pg_class WHERE oid = 'identity.users'::regclass",
                     "SELECT relallvisible FROM pg_class WHERE oid = 'identity.users'::regclass",
                     "SELECT * FROM pg_class WHERE oid = 'identity.users'::regclass",
                     "SELECT sum(n_live_tup) FROM pg_stat_user_tables",
                     "SELECT count(*) FROM pg_stat_activity",
                     "SELECT count(*) FROM pg_stats",
                     "SELECT pg_stat_get_live_tuples('identity.users'::regclass)",
                     "SELECT pg_total_relation_size('identity.users'::regclass)",
                     "SELECT pg_database_size(current_database())",
                 })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => DbCatalog.ExecuteAsync(app, sql));
            Assert.True(error.SqlState == PostgresErrorCodes.InsufficientPrivilege, $"{sql}: {error.SqlState} {error.MessageText}");
        }
    }

    /// <summary>
    /// Product code never asks PostgreSQL for figures that describe whole tables: the statistics
    /// views and functions (closed to the application role, above), and pg_class's row and page
    /// estimates, which every role can read (the catalog is needed by every client library). A
    /// list total, an "activity" counter or a grouping count taken from them would count every
    /// tenant (critic p00 round 8). Only the bootstrap that takes them from PUBLIC may name them.
    /// </summary>
    [Fact]
    public void Product_code_never_reads_statistics_that_count_every_tenant()
    {
        var banned = new System.Text.RegularExpressions.Regex(
            @"\bpg_stat|\bpg_statio|\bn_live_tup\b|\bn_dead_tup\b|\bn_tup_(ins|upd|del|hot_upd)\b|\breltuples\b|\brelpages\b|\brelallvisible\b|\bpg_(total_)?relation_size\b|\bpg_table_size\b|\bpg_indexes_size\b|\bpg_database_size\b|\bpg_tablespace_size\b|\bpg_class_size\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // The one place allowed to name them: the bootstrap SQL that takes them from PUBLIC.
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "src/Kernel/Erp.Kernel/Data/KernelSql.cs" };
        var files = Directory.EnumerateFiles(Repo.PathOf("src"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".sql", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.True(files.Count >= 100, $"only {files.Count} product source files found: the scan is blind");
        var found = new List<string>();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(Repo.Root, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (banned.IsMatch(lines[i]) && !allowed.Contains(relative))
                {
                    found.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }
        Assert.True(found.Count == 0, "Product code reads figures that count every tenant's rows:\n" + string.Join("\n", found));
        // Self-check: the pattern finds the round-8 plant.
        Assert.Matches(banned, "SELECT n_live_tup FROM pg_stat_user_tables WHERE relname = 'users'");
        Assert.Matches(banned, "SELECT reltuples::bigint FROM pg_class WHERE oid = 'identity.users'::regclass");
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
    public async Task Leakproof_functions_are_postgres_defaults_or_reviewed()
    {
        // A fresh database on the same server shows PostgreSQL's own leakproof set.
        var fresh = new NpgsqlConnectionStringBuilder(Env.AdminConnectionString) { Database = "postgres" }.ConnectionString;
        const string sql = """
            SELECT n.nspname || '.' || p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ')'
              FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE p.proleakproof
            """;
        await using var reference = new NpgsqlConnection(fresh);
        await reference.OpenAsync();
        var defaults = (await DbCatalog.ReadAsync(reference, sql, r => r.GetString(0))).ToHashSet(StringComparer.Ordinal);
        await using var admin = await Env.OpenAdminAsync();
        var actual = await DbCatalog.ReadAsync(admin, sql, r => r.GetString(0));
        Assert.True(defaults.Count > 100, $"Only {defaults.Count} leakproof functions in a fresh database; the comparison is blind");
        var changed = actual.Where(f => !defaults.Contains(f)).Order(StringComparer.Ordinal).ToList();
        var reviewed = Repo.ReadReviewedList("tests/Gates/leakproof-allowlist.txt");
        Assert.All(reviewed, r => Assert.False(string.IsNullOrWhiteSpace(r.Reason), $"{r.Entry} needs a reason"));
        Assert.Equal(reviewed.Select(r => r.Entry).Order(StringComparer.Ordinal), changed);
        Assert.True(changed.Count <= Ratchet.Max("g1.leakproofChanges"),
            $"{changed.Count} functions marked leakproof beyond PostgreSQL's defaults; ratchet maximum {Ratchet.Max("g1.leakproofChanges")}");
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
                // Other gate tests of this fixture write tenant A rows (sign-ins leave audit rows) while
                // this one runs, so the count tenant A owns is read again around what it sees: a count
                // that moved meanwhile is measured again, and only a stable difference is a problem.
                var visible = await DbCatalog.ScalarAsync<long>(app, $"SELECT count(*) FROM {name}");
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    var before = await DbCatalog.ScalarAsync<long>(admin, $"SELECT count(*) FROM {name} WHERE tenant_id = @a", ("a", a));
                    await using var probe = await Env.OpenAppAsync();
                    await using var probeTx = await probe.BeginTransactionAsync();
                    await BindAsync(probe, probeTx, a);
                    visible = await DbCatalog.ScalarAsync<long>(probe, $"SELECT count(*) FROM {name}");
                    await probeTx.RollbackAsync();
                    countA = await DbCatalog.ScalarAsync<long>(admin, $"SELECT count(*) FROM {name} WHERE tenant_id = @a", ("a", a));
                    if (before == countA)
                    {
                        break;
                    }
                }
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
