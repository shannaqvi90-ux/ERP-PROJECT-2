using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Data;
using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, company layer in the database. A tenant is divided into companies; a user works only in
/// the companies they were given. Every tenant table whose rows belong to a company (a
/// <c>company_id</c> column) carries the standard RESTRICTIVE <c>company_scope</c> policy, so
/// the database itself shows and changes only rows of the companies bound to the transaction:
/// none without a binding, none from a binding left at session level or by an earlier
/// transaction, and never a row moved or inserted into a company outside the binding.
/// </summary>
public sealed class G1CompanyScopeTests(GateFixture fixture)
{
    private ErpTestEnvironment Env => fixture.Env;

    private const string Standard = "erp.company_allowed(company_id)";

    /// <summary>Own-row tables whose own rows outside the scope their user's session may move or
    /// delete (reviewed, with the reason).</summary>
    private static readonly IReadOnlySet<string> OwnRowWrites = Repo.ReadReviewedList("tests/Gates/own-row-writes.txt").Select(e => e.Entry).ToHashSet(StringComparer.Ordinal);
    private const string StandardOrOwn = "(erp.company_allowed(company_id) OR (user_id = erp.current_actor_id()))";

    [Fact]
    public async Task Every_company_table_carries_the_standard_company_scope_policy()
    {
        var (problems, tables) = await PolicyProblemsAsync(Env);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(tables >= Ratchet.Min("g1.companyTables"), $"g1.companyTables: {tables}; ratchet minimum {Ratchet.Min("g1.companyTables")}");
    }

    /// <summary>Structural check of every company table (also run by the self-tests).</summary>
    public static async Task<(List<string> Problems, int Tables)> PolicyProblemsAsync(ErpTestEnvironment env)
    {
        await using var admin = await env.OpenAdminAsync();
        var problems = new List<string>();
        var tables = await DbCatalog.CompanyTablesAsync(admin);
        foreach (var table in tables)
        {
            var columns = await DbCatalog.ColumnsAsync(admin, table);
            var company = columns.Single(c => c.Name == "company_id");
            if (company.Type != "uuid" || !company.NotNull || company.Generated)
            {
                problems.Add($"{table}: company_id must be a stored uuid NOT NULL");
            }
            var policies = await DbCatalog.ReadAsync(admin, """
                SELECT p.polname, p.polcmd::text, p.polpermissive,
                       ARRAY(SELECT CASE WHEN r = 0 THEN 'public' ELSE pg_get_userbyid(r) END FROM unnest(p.polroles) r),
                       pg_get_expr(p.polqual, p.polrelid), pg_get_expr(p.polwithcheck, p.polrelid)
                  FROM pg_policy p WHERE p.polrelid = to_regclass(@t) AND p.polname = 'company_scope'
                """, r => (Command: r.GetString(1), Permissive: r.GetBoolean(2), Roles: r.GetFieldValue<string[]>(3),
                    Using: r.IsDBNull(4) ? null : r.GetString(4), Check: r.IsDBNull(5) ? null : r.GetString(5)),
                ("t", table.Qualified));
            if (policies.Count != 1)
            {
                problems.Add($"{table}: needs exactly one company_scope policy (migrationBuilder.ProtectCompanyTable)");
                continue;
            }
            var policy = policies[0];
            // The own-rows variant lets a user's session read its own access rows before its scope
            // exists; it is allowed only on tables keyed by user_id, and never for writing.
            var usingAllowed = policy.Using == Standard || (policy.Using == StandardOrOwn && columns.Any(c => c.Name == "user_id"));
            if (policy.Command != "*" || policy.Permissive || policy.Roles is not ["public"] || !usingAllowed || policy.Check != Standard)
            {
                problems.Add($"{table}: company_scope must be RESTRICTIVE FOR ALL TO PUBLIC USING {Standard} (or {StandardOrOwn} on a user_id table) " +
                             $"WITH CHECK {Standard} (is {policy.Command} permissive={policy.Permissive} [{string.Join(",", policy.Roles)}] {policy.Using} / {policy.Check})");
            }
            // Own rows readable outside the scope are never written there: RESTRICTIVE policies for
            // UPDATE and DELETE with the standard expression (migrationBuilder.KeepOwnRowsReadOnly),
            // unless the table is reviewed in tests/Gates/own-row-writes.txt.
            if (policy.Using == StandardOrOwn && !OwnRowWrites.Contains(table.Qualified))
            {
                foreach (var (command, policyName) in new[] { ("w", "company_scope_update"), ("d", "company_scope_delete") })
                {
                    var write = await DbCatalog.ReadAsync(admin,
                        "SELECT p.polcmd::text, p.polpermissive, pg_get_expr(p.polqual, p.polrelid), " +
                        "ARRAY(SELECT CASE WHEN r = 0 THEN 'public' ELSE pg_get_userbyid(r) END FROM unnest(p.polroles) r) " +
                        "FROM pg_policy p WHERE p.polrelid = to_regclass(@t) AND p.polname = @n",
                        r => (Command: r.GetString(0), Permissive: r.GetBoolean(1), Using: r.IsDBNull(2) ? null : r.GetString(2), Roles: r.GetFieldValue<string[]>(3)),
                        ("t", table.Qualified), ("n", policyName));
                    if (write.Count != 1 || write[0].Permissive || write[0].Roles is not ["public"] || write[0].Command != command || write[0].Using != Standard)
                    {
                        problems.Add($"{table}: its own rows are readable outside the company scope, so it needs the RESTRICTIVE {policyName} policy " +
                                     $"(FOR {(command == "w" ? "UPDATE" : "DELETE")} TO PUBLIC USING {Standard}; migrationBuilder.KeepOwnRowsReadOnly) or a reviewed entry in tests/Gates/own-row-writes.txt");
                    }
                }
            }
        }
        // The company table itself: a company's company_id is its own id.
        var self = await DbCatalog.ScalarAsync<long>(admin, "SELECT count(*) FROM tenancy.companies WHERE company_id <> id");
        if (self != 0) problems.Add($"tenancy.companies: {self} rows whose company_id is not their id");
        return (problems, tables.Count);
    }

    [Fact]
    public async Task The_application_role_sees_and_changes_only_the_bound_companies_in_every_company_table()
    {
        var tenant = Env.TenantA.Id;
        await using var admin = await Env.OpenAdminAsync();
        var companies = await DbCatalog.ReadAsync(admin, "SELECT id FROM tenancy.companies WHERE tenant_id = @t ORDER BY id", r => r.GetGuid(0), ("t", tenant));
        Assert.True(companies.Count >= 2, "the gate fixture needs two companies in tenant A");
        var (x, y) = (companies[0], companies[1]);
        var tables = await DbCatalog.CompanyTablesAsync(admin);
        var problems = new List<string>();
        var stranger = Guid.NewGuid();

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
            await using var app = await Env.OpenAppAsync();

            // Tenant bound, no company bound (a signed-in user before their scope is known): nothing.
            await using (var tx = await app.BeginTransactionAsync())
            {
                await BindAsync(app, tx, tenant, stranger, scope: "none", companies: []);
                var seen = await DbCatalog.ScalarAsync<long>(app, $"SELECT count(*) FROM {name}");
                if (seen != 0) problems.Add($"{name}: {seen} rows visible with no company bound");
                await tx.RollbackAsync();
            }

            // A company scope left at session level (or by an earlier transaction) binds nothing.
            await using (var tx = await app.BeginTransactionAsync())
            {
                await using (var leak = new NpgsqlCommand(
                    "SELECT set_config('app.company_scope', 'all', false), set_config('app.company_tx', extract(epoch from now())::text, false)", app, tx))
                {
                    await leak.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
            }
            await using (var tx = await app.BeginTransactionAsync())
            {
                await G1DatabaseIsolationTests_Bind(app, tx, tenant);
                var seen = await DbCatalog.ScalarAsync<long>(app, $"SELECT count(*) FROM {name}");
                if (seen != 0) problems.Add($"{name}: {seen} rows visible through a company scope left by an earlier transaction");
                await tx.RollbackAsync();
            }

            // Company X bound: exactly X's rows; Y's rows cannot be read, changed, deleted, moved into or inserted.
            await using (var tx = await app.BeginTransactionAsync())
            {
                await BindAsync(app, tx, tenant, stranger, scope: "list", companies: [x]);
                var seen = await DbCatalog.ScalarAsync<long>(app, $"SELECT count(*) FROM {name}");
                var foreign = await DbCatalog.ScalarAsync<long>(app, $"SELECT count(*) FROM {name} WHERE company_id <> '{x}'");
                if (seen != inX) problems.Add($"{name}: with company X bound {seen} rows visible, X owns {inX}");
                if (foreign != 0) problems.Add($"{name}: with company X bound {foreign} rows of other companies visible");
                var updated = await ExecAsync(app, tx, $"UPDATE {name} SET company_id = company_id WHERE company_id = '{y}'");
                if (updated != 0) problems.Add($"{name}: company X updated {updated} rows of company Y");
                var deleted = await ExecAsync(app, tx, $"DELETE FROM {name} WHERE company_id = '{y}'");
                if (deleted != 0) problems.Add($"{name}: company X deleted {deleted} rows of company Y");
                await ExpectDenied(app, tx, problems, $"{name}: moving a row of X into Y",
                    $"UPDATE {name} SET company_id = '{y}' WHERE id = (SELECT id FROM {name} LIMIT 1)");
                var overrides = $"jsonb_build_object('id', gen_random_uuid(), 'company_id', '{y}'::uuid)";
                await ExpectDenied(app, tx, problems, $"{name}: inserting a row into Y",
                    $"INSERT INTO {name} ({string.Join(", ", insertable)}) SELECT {string.Join(", ", insertable.Select(c => "r." + c))} " +
                    $"FROM {name} x, LATERAL jsonb_populate_record(NULL::{name}, to_jsonb(x) || {overrides}) r LIMIT 1");
                await tx.RollbackAsync();
            }

            // A user's own rows in a table that lets the session read them before its scope exists
            // stay read-only outside the scope.
            if (columns.Any(c => c.Name == "user_id"))
            {
                var owner = await DbCatalog.ReadAsync(admin, $"SELECT user_id FROM {name} WHERE tenant_id = @t AND company_id = @c LIMIT 1", r => r.GetGuid(0), ("t", tenant), ("c", y));
                await using var tx = await app.BeginTransactionAsync();
                await BindAsync(app, tx, tenant, owner[0], scope: "list", companies: [x]);
                // The row stays readable to its user (the own-rows clause), so a write affecting no
                // row was refused by a write policy, not missed for want of a row; a table without
                // the write policies must refuse it outright (the check option).
                var readOnly = !OwnRowWrites.Contains(name);
                var own = await DbCatalog.ScalarAsync<long>(app, $"SELECT count(*) FROM {name} WHERE user_id = '{owner[0]}' AND company_id = '{y}'");
                if (own == 0) problems.Add($"{name}: a user's own row of a company outside their scope is not readable to them, so the own-row checks prove nothing");
                await ExpectDenied(app, tx, problems, $"{name}: a user changing their own row of a company outside their scope",
                    $"UPDATE {name} SET company_id = company_id WHERE user_id = '{owner[0]}' AND company_id = '{y}'", allowNoRows: readOnly);
                // Nor moved into the scope (a role held in Y becoming one held in X), nor deleted
                // (critic p03 round 6), unless reviewed: a table whose own rows outside the scope
                // its user's session removes (a workplace left in a company the user no longer
                // works in) is listed in tests/Gates/own-row-writes.txt with the reason.
                if (readOnly)
                {
                    await ExpectDenied(app, tx, problems, $"{name}: a user moving their own row of a company outside their scope into it",
                        $"UPDATE {name} SET company_id = '{x}' WHERE user_id = '{owner[0]}' AND company_id = '{y}'", allowNoRows: true);
                    await ExpectDenied(app, tx, problems, $"{name}: a user deleting their own row of a company outside their scope",
                        $"DELETE FROM {name} WHERE user_id = '{owner[0]}' AND company_id = '{y}'", allowNoRows: true);
                    var after = await DbCatalog.ScalarAsync<long>(app, $"SELECT count(*) FROM {name} WHERE user_id = '{owner[0]}' AND company_id = '{y}'");
                    if (after != own) problems.Add($"{name}: a user's own rows of a company outside their scope went from {own} to {after}");
                }
                await tx.RollbackAsync();
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(tables.Count >= Ratchet.Min("g1.companyTables"));
    }

    /// <summary>Bind tenant, actor and company scope the way <see cref="ErpDbSession"/> does.</summary>
    internal static async Task BindAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenant, Guid actor, string scope, IReadOnlyList<Guid> companies)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @t, true), set_config('app.tenant_tx', extract(epoch from now())::text, true), " +
            "set_config('app.actor_id', @a, true), set_config('app.actor_kind', 'user', true), " +
            "set_config('app.company_scope', @s, true), set_config('app.company_ids', @c, true), " +
            "set_config('app.company_tx', extract(epoch from now())::text, true)", connection, tx);
        command.Parameters.AddWithValue("t", tenant.ToString());
        command.Parameters.AddWithValue("a", actor.ToString());
        command.Parameters.AddWithValue("s", scope);
        command.Parameters.AddWithValue("c", string.Join(',', companies));
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Binds only the tenant (as a careless caller would), leaving the company scope to
    /// whatever the connection carries.</summary>
    private static async Task G1DatabaseIsolationTests_Bind(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenant)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @t, true), set_config('app.tenant_tx', extract(epoch from now())::text, true)", connection, tx);
        command.Parameters.AddWithValue("t", tenant.ToString());
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
        finally
        {
            await tx.RollbackAsync("attempt");
        }
    }

    /// <summary>The statement must fail with insufficient privilege (row-level security).</summary>
    private static async Task ExpectDenied(NpgsqlConnection connection, NpgsqlTransaction tx, List<string> problems, string what, string sql, bool allowNoRows = false)
    {
        await tx.SaveAsync("attempt");
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            var rows = await command.ExecuteNonQueryAsync();
            if (!(allowNoRows && rows == 0))
            {
                problems.Add($"{what}: succeeded ({rows} rows)");
            }
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
}
