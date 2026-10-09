using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Erp.Kernel.Data;

/// <summary>Database role names. Created by the bootstrap step (see <see cref="DatabaseBootstrap"/>).</summary>
public static class DatabaseRoles
{
    /// <summary>Owns every schema and table; runs migrations. Not superuser, no BYPASSRLS, and
    /// row-level security is forced on its tables too.</summary>
    public const string Owner = "erp_owner";

    /// <summary>The role the application connects as: not superuser, not owner, no BYPASSRLS,
    /// member of nothing.</summary>
    public const string App = "erp_app";

    /// <summary>NOLOGIN role that owns the reviewed security-definer lookups which resolve a tenant
    /// before one is known (sign-in by e-mail, session by token hash).</summary>
    public const string AuthResolver = "erp_auth";
}

/// <summary>Migration helpers every module uses for its tables.</summary>
public static partial class TenantSql
{
    /// <summary>
    /// Make a table tenant-safe: enable and force row-level security with the standard
    /// <c>tenant_isolation</c> policy, grant the application role DML, and (unless
    /// <paramref name="audited"/> is false) attach the audit trigger. Columns in
    /// <paramref name="redact"/> are recorded as "[redacted]" in the audit trail; columns in
    /// <paramref name="ignore"/> are left out of the change set.
    /// </summary>
    public static void ProtectTenantTable(this MigrationBuilder migration, string schema, string table, bool audited = true,
        string[]? redact = null, string[]? ignore = null)
    {
        redact ??= [];
        ignore ??= [];
        var name = Qualified(schema, table);
        migration.Sql($"ALTER TABLE {name} ENABLE ROW LEVEL SECURITY;");
        migration.Sql($"ALTER TABLE {name} FORCE ROW LEVEL SECURITY;");
        migration.Sql($"CREATE POLICY tenant_isolation ON {name} AS PERMISSIVE FOR ALL TO PUBLIC " +
                      "USING (tenant_id = erp.current_tenant_id()) WITH CHECK (tenant_id = erp.current_tenant_id());");
        migration.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON {name} TO {DatabaseRoles.App};");
        if (audited)
        {
            var args = string.Join(", ", redact.Select(c => $"'{Identifier(c)}'").Concat(ignore.Select(c => $"'-{Identifier(c)}'")));
            migration.Sql($"CREATE TRIGGER audit_capture AFTER INSERT OR UPDATE OR DELETE ON {name} " +
                          $"FOR EACH ROW EXECUTE FUNCTION audit.capture({args});");
        }
    }

    /// <summary>Reverse of <see cref="ProtectTenantTable"/> for Down migrations.</summary>
    public static void UnprotectTenantTable(this MigrationBuilder migration, string schema, string table)
    {
        var name = Qualified(schema, table);
        migration.Sql($"DROP TRIGGER IF EXISTS audit_capture ON {name};");
        migration.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {name};");
        migration.Sql($"REVOKE ALL ON {name} FROM {DatabaseRoles.App};");
    }

    /// <summary>The standard company-scope expression (the company gate compares policies with it).</summary>
    public const string CompanyScopeExpression = "erp.company_allowed(company_id)";

    /// <summary>
    /// Scope a tenant table whose rows belong to a company (call after
    /// <see cref="ProtectTenantTable"/>): a RESTRICTIVE <c>company_scope</c> policy, so a row is
    /// visible and writable only when its <c>company_id</c> is in the unit of work's company scope,
    /// on top of the tenant policy. With <paramref name="ownRowsReadable"/> the signed-in user's own
    /// rows (column <c>user_id</c>) stay readable, never writable, outside the scope: the session
    /// reads its own company access before its scope exists.
    /// </summary>
    public static void ProtectCompanyTable(this MigrationBuilder migration, string schema, string table, bool ownRowsReadable = false)
    {
        var name = Qualified(schema, table);
        var visible = ownRowsReadable ? $"{CompanyScopeExpression} OR user_id = erp.current_actor_id()" : CompanyScopeExpression;
        migration.Sql($"CREATE POLICY company_scope ON {name} AS RESTRICTIVE FOR ALL TO PUBLIC " +
                      $"USING ({visible}) WITH CHECK ({CompanyScopeExpression});");
    }

    /// <summary>
    /// For a table scoped with <c>ownRowsReadable</c> (see <see cref="ProtectCompanyTable"/>): the
    /// user's own rows outside the company scope stay readable but are never moved or deleted
    /// there. RESTRICTIVE <c>company_scope_update</c> (FOR UPDATE) and <c>company_scope_delete</c>
    /// (FOR DELETE) policies with the standard expression: without them the own-rows reading
    /// clause also let the session delete its own rows of other companies, or move one into its
    /// scope (a role held in Y becoming one held in X), since UPDATE and DELETE read rows through
    /// the FOR ALL policy's USING clause (critic p03 round 6). Foreign-key cascades are not
    /// affected (referential actions bypass row security).
    /// </summary>
    public static void KeepOwnRowsReadOnly(this MigrationBuilder migration, string schema, string table)
    {
        var name = Qualified(schema, table);
        migration.Sql($"DROP POLICY IF EXISTS company_scope_update ON {name};");
        migration.Sql($"DROP POLICY IF EXISTS company_scope_delete ON {name};");
        migration.Sql($"CREATE POLICY company_scope_update ON {name} AS RESTRICTIVE FOR UPDATE TO PUBLIC USING ({CompanyScopeExpression});");
        migration.Sql($"CREATE POLICY company_scope_delete ON {name} AS RESTRICTIVE FOR DELETE TO PUBLIC USING ({CompanyScopeExpression});");
    }

    /// <summary>Reverse of <see cref="KeepOwnRowsReadOnly"/>.</summary>
    public static void AllowOwnRowWrites(this MigrationBuilder migration, string schema, string table)
    {
        var name = Qualified(schema, table);
        migration.Sql($"DROP POLICY IF EXISTS company_scope_update ON {name};");
        migration.Sql($"DROP POLICY IF EXISTS company_scope_delete ON {name};");
    }

    /// <summary>Reverse of <see cref="ProtectCompanyTable"/>.</summary>
    public static void UnprotectCompanyTable(this MigrationBuilder migration, string schema, string table) =>
        migration.Sql($"DROP POLICY IF EXISTS company_scope ON {Qualified(schema, table)};");

    /// <summary>Let the application role use the module's schema (no CREATE).</summary>
    public static void GrantSchemaUsage(this MigrationBuilder migration, string schema)
    {
        migration.Sql($"GRANT USAGE ON SCHEMA {Identifier(schema)} TO {DatabaseRoles.App};");
        migration.Sql($"REVOKE CREATE ON SCHEMA {Identifier(schema)} FROM PUBLIC;");
    }

    public static string Qualified(string schema, string table) => $"{Identifier(schema)}.{Identifier(table)}";

    /// <summary>Validate a SQL identifier (lower snake case only, so it never needs quoting).</summary>
    public static string Identifier(string value) => IdentifierRegex().IsMatch(value)
        ? value
        : throw new ArgumentException($"'{value}' is not a lower snake case identifier.", nameof(value));

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex IdentifierRegex();
}
