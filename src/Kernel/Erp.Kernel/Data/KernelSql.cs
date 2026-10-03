namespace Erp.Kernel.Data;

/// <summary>SQL for the kernel's database objects, applied by the kernel migrations.</summary>
internal static class KernelSql
{
    /// <summary>Helper functions row-level security policies call. The tenant setting is
    /// transaction-local (set by <see cref="ErpDbSession.BeginAsync"/>); when it is missing the
    /// functions return NULL and every policy compares false (fail closed).</summary>
    public const string Functions = """
        CREATE SCHEMA IF NOT EXISTS erp;
        REVOKE CREATE ON SCHEMA erp FROM PUBLIC;
        GRANT USAGE ON SCHEMA erp TO erp_app;

        CREATE OR REPLACE FUNCTION erp.current_tenant_id() RETURNS uuid
            LANGUAGE sql STABLE PARALLEL SAFE
            AS $$ SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid $$;

        CREATE OR REPLACE FUNCTION erp.current_actor_id() RETURNS uuid
            LANGUAGE sql STABLE PARALLEL SAFE
            AS $$ SELECT NULLIF(current_setting('app.actor_id', true), '')::uuid $$;

        GRANT EXECUTE ON FUNCTION erp.current_tenant_id() TO erp_app;
        GRANT EXECUTE ON FUNCTION erp.current_actor_id() TO erp_app;
        """;

    /// <summary>The audit trail is append-only for the application: read and insert within its
    /// tenant, never update or delete.</summary>
    public const string AuditSecurity = """
        REVOKE CREATE ON SCHEMA audit FROM PUBLIC;
        GRANT USAGE ON SCHEMA audit TO erp_app;
        ALTER TABLE audit.entries ENABLE ROW LEVEL SECURITY;
        ALTER TABLE audit.entries FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_isolation ON audit.entries AS PERMISSIVE FOR ALL TO PUBLIC
            USING (tenant_id = erp.current_tenant_id()) WITH CHECK (tenant_id = erp.current_tenant_id());
        GRANT SELECT, INSERT ON audit.entries TO erp_app;
        """;

    /// <summary>
    /// Row trigger attached to every audited table. Records the actor (from the transaction's
    /// settings), the operation and the field-level change set as
    /// <c>{"column": {"old": …, "new": …}}</c>. Updates that change nothing are not recorded.
    /// Columns passed as trigger arguments are recorded as "[redacted]"; arguments starting with
    /// "-" name columns left out of the change set (high-churn bookkeeping such as the last
    /// sign-in time, which has its own history). Timestamps and the tenant are always left out.
    /// </summary>
    public const string CaptureFunction = """
        CREATE OR REPLACE FUNCTION audit.capture() RETURNS trigger
            LANGUAGE plpgsql
            SET search_path = pg_catalog, pg_temp
        AS $$
        DECLARE
            v_old jsonb := CASE WHEN TG_OP IN ('UPDATE', 'DELETE') THEN to_jsonb(OLD) END;
            v_new jsonb := CASE WHEN TG_OP IN ('INSERT', 'UPDATE') THEN to_jsonb(NEW) END;
            v_row jsonb := COALESCE(v_new, v_old);
            v_args text[] := COALESCE(TG_ARGV::text[], ARRAY[]::text[]);
            v_redact text[] := ARRAY(SELECT a FROM unnest(v_args) AS a WHERE a NOT LIKE '-%');
            v_skip text[] := ARRAY['tenant_id', 'created_at', 'created_by', 'updated_at', 'updated_by']
                             || ARRAY(SELECT substr(a, 2) FROM unnest(v_args) AS a WHERE a LIKE '-%');
            v_changes jsonb;
        BEGIN
            SELECT jsonb_object_agg(k.key, jsonb_strip_nulls(jsonb_build_object(
                       'old', CASE WHEN k.key = ANY (v_redact) AND v_old ? k.key AND v_old -> k.key <> 'null'::jsonb THEN '"[redacted]"'::jsonb ELSE v_old -> k.key END,
                       'new', CASE WHEN k.key = ANY (v_redact) AND v_new ? k.key AND v_new -> k.key <> 'null'::jsonb THEN '"[redacted]"'::jsonb ELSE v_new -> k.key END)))
              INTO v_changes
              FROM jsonb_object_keys(v_row) AS k(key)
             WHERE k.key <> ALL (v_skip)
               AND NOT (COALESCE(v_old -> k.key, 'null'::jsonb) = 'null'::jsonb AND COALESCE(v_new -> k.key, 'null'::jsonb) = 'null'::jsonb)
               AND (TG_OP <> 'UPDATE' OR (v_old -> k.key) IS DISTINCT FROM (v_new -> k.key));

            IF TG_OP = 'UPDATE' AND v_changes IS NULL THEN
                RETURN NULL;
            END IF;

            INSERT INTO audit.entries (tenant_id, occurred_at, actor_id, actor_kind, table_schema, table_name,
                                       record_id, action, changes, transaction_id, correlation_id)
            VALUES ((v_row ->> 'tenant_id')::uuid,
                    now(),
                    erp.current_actor_id(),
                    COALESCE(NULLIF(current_setting('app.actor_kind', true), ''), 'unknown'),
                    TG_TABLE_SCHEMA,
                    TG_TABLE_NAME,
                    (v_row ->> 'id')::uuid,
                    lower(TG_OP),
                    COALESCE(v_changes, '{}'::jsonb),
                    txid_current(),
                    NULLIF(current_setting('app.correlation_id', true), ''));
            RETURN NULL;
        END;
        $$;
        REVOKE ALL ON FUNCTION audit.capture() FROM PUBLIC;
        """;

    public const string Down = """
        DROP FUNCTION IF EXISTS audit.capture();
        DROP POLICY IF EXISTS tenant_isolation ON audit.entries;
        DROP FUNCTION IF EXISTS erp.current_actor_id();
        DROP FUNCTION IF EXISTS erp.current_tenant_id();
        DROP SCHEMA IF EXISTS erp;
        """;

    /// <summary>
    /// The tenant counts only inside the transaction that bound it. Binding sets
    /// <c>app.tenant_id</c> and <c>app.tenant_tx</c> (the transaction's start time) transaction-
    /// locally; a tenant left at session level on a pooled connection, or set without the marker,
    /// binds nothing.
    /// </summary>
    public const string TransactionBoundTenant = """
        CREATE OR REPLACE FUNCTION erp.current_tenant_id() RETURNS uuid
            LANGUAGE sql STABLE PARALLEL SAFE
            AS $$
                SELECT CASE WHEN current_setting('app.tenant_tx', true) = extract(epoch from now())::text
                            THEN NULLIF(current_setting('app.tenant_id', true), '')::uuid END
            $$;
        """;

    public const string TransactionBoundTenantDown = """
        CREATE OR REPLACE FUNCTION erp.current_tenant_id() RETURNS uuid
            LANGUAGE sql STABLE PARALLEL SAFE
            AS $$ SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid $$;
        """;

    /// <summary>
    /// Company scope: the second, within-tenant layer of row-level security. A table whose rows
    /// belong to a company carries <c>company_id</c> and a RESTRICTIVE <c>company_scope</c> policy
    /// (see <see cref="TenantSql.ProtectCompanyTable"/>), so a row is visible only when its tenant is
    /// bound AND its company is in the unit of work's company scope. The scope is transaction-local
    /// like the tenant: <c>app.company_scope</c> is <c>all</c> (system work: seeding, jobs, operator
    /// commands), <c>list</c> (a signed-in user: the companies in <c>app.company_ids</c>) or
    /// anything else (nothing), and it counts only together with <c>app.company_tx</c>, the
    /// transaction's start time. Missing settings fail closed.
    /// </summary>
    public const string CompanyScope = """
        CREATE OR REPLACE FUNCTION erp.current_company_scope() RETURNS text
            LANGUAGE sql STABLE PARALLEL SAFE
            AS $$
                SELECT CASE WHEN erp.current_tenant_id() IS NOT NULL
                             AND current_setting('app.company_tx', true) = extract(epoch from now())::text
                            THEN current_setting('app.company_scope', true) END
            $$;

        CREATE OR REPLACE FUNCTION erp.current_company_ids() RETURNS uuid[]
            LANGUAGE sql STABLE PARALLEL SAFE
            AS $$
                SELECT CASE WHEN erp.current_company_scope() = 'list'
                            THEN string_to_array(NULLIF(current_setting('app.company_ids', true), ''), ',')::uuid[] END
            $$;

        CREATE OR REPLACE FUNCTION erp.company_allowed(p_company_id uuid) RETURNS boolean
            LANGUAGE sql STABLE PARALLEL SAFE
            AS $$
                SELECT coalesce(erp.current_company_scope() = 'all'
                             OR (erp.current_company_scope() = 'list' AND p_company_id = ANY (erp.current_company_ids())), false)
            $$;

        GRANT EXECUTE ON FUNCTION erp.current_company_scope() TO erp_app;
        GRANT EXECUTE ON FUNCTION erp.current_company_ids() TO erp_app;
        GRANT EXECUTE ON FUNCTION erp.company_allowed(uuid) TO erp_app;
        """;

    public const string CompanyScopeDown = """
        DROP FUNCTION IF EXISTS erp.company_allowed(uuid);
        DROP FUNCTION IF EXISTS erp.current_company_ids();
        DROP FUNCTION IF EXISTS erp.current_company_scope();
        """;
}
