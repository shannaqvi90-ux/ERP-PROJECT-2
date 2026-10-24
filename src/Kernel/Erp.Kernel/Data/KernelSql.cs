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
    /// The names of the fields an audit row records: the keys of its change set, sorted. An
    /// IMMUTABLE function of the row's own value, so the stored column <c>audit.entries.fields</c>
    /// is generated from it and a GIN index serves "rows that changed this field".
    /// </summary>
    public const string FieldNamesFunction = """
        CREATE OR REPLACE FUNCTION audit.field_names(p_changes jsonb) RETURNS text[]
            LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
            SET search_path = pg_catalog, pg_temp
            AS $$ SELECT array_agg(k ORDER BY k) FROM jsonb_object_keys(p_changes) AS k $$;
        REVOKE ALL ON FUNCTION audit.field_names(jsonb) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION audit.field_names(jsonb) TO erp_app;
        """;

    public const string FieldNamesFunctionDown = """
        DROP FUNCTION IF EXISTS audit.field_names(jsonb);
        """;

    /// <summary>The company layer of the audit trail (p07): a row about a record of a company
    /// (<c>record_company_id</c>) is readable only inside the reader's company scope, as the record
    /// itself is; a row about a record of the whole workspace (no company) is not narrowed. Reads
    /// only: the capture trigger inserts rows for every company the writing statement reaches (a
    /// foreign-key cascade included), and nothing may update or delete them.</summary>
    public const string AuditCompanyScope = """
        CREATE POLICY audit_company_scope ON audit.entries AS RESTRICTIVE FOR SELECT TO PUBLIC
            USING (record_company_id IS NULL OR erp.company_allowed(record_company_id));
        """;

    public const string AuditCompanyScopeDown = """
        DROP POLICY IF EXISTS audit_company_scope ON audit.entries;
        """;

    /// <summary>The expression of <see cref="AuditCompanyScope"/> as PostgreSQL prints it back
    /// (<c>pg_get_expr</c>); the gates compare the policy with it exactly.</summary>
    public const string AuditCompanyScopeExpression = "((record_company_id IS NULL) OR erp.company_allowed(record_company_id))";

    /// <summary>
    /// The capture trigger, second version (p07, audit trail screens). Besides what the first
    /// version records it:
    /// <list type="bullet">
    /// <item>names the record as it was when it changed: <c>record_label</c> from its display name,
    /// English name, legal name, name, code or e-mail (the first one it has), <c>record_label_ar</c>
    /// from its Arabic display, name or legal name; at most 200 characters each;</item>
    /// <item>keeps the record's company (<c>record_company_id</c>, its <c>company_id</c>), which the
    /// <c>audit_company_scope</c> policy reads, and its branch (<c>record_branch_id</c>, its
    /// <c>branch_id</c>), by which the audit screens show a branch-limited reader only their branches;</item>
    /// <item>records tenant-defined custom fields (a JSON object column named
    /// <c>custom_fields</c>) field by field, as <c>custom_fields.&lt;key&gt;</c> with their own old
    /// and new values, exactly like the record's own columns;</item>
    /// <item>takes the time from the clock (<c>clock_timestamp()</c>), not the transaction's start,
    /// so the changes of one transaction keep their order.</item>
    /// </list>
    /// </summary>
    public const string CaptureFunctionV2 = """
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
            v_label text;
            v_label_ar text;
        BEGIN
            -- Custom fields are recorded one by one, as the record's own fields are.
            IF jsonb_typeof(v_old -> 'custom_fields') = 'object' THEN
                v_old := (v_old - 'custom_fields') || COALESCE(
                    (SELECT jsonb_object_agg('custom_fields.' || c.key, c.value) FROM jsonb_each(v_old -> 'custom_fields') AS c), '{}'::jsonb);
            END IF;
            IF jsonb_typeof(v_new -> 'custom_fields') = 'object' THEN
                v_new := (v_new - 'custom_fields') || COALESCE(
                    (SELECT jsonb_object_agg('custom_fields.' || c.key, c.value) FROM jsonb_each(v_new -> 'custom_fields') AS c), '{}'::jsonb);
            END IF;

            SELECT jsonb_object_agg(k.key, jsonb_strip_nulls(jsonb_build_object(
                       'old', CASE WHEN k.key = ANY (v_redact) AND v_old ? k.key AND v_old -> k.key <> 'null'::jsonb THEN '"[redacted]"'::jsonb ELSE v_old -> k.key END,
                       'new', CASE WHEN k.key = ANY (v_redact) AND v_new ? k.key AND v_new -> k.key <> 'null'::jsonb THEN '"[redacted]"'::jsonb ELSE v_new -> k.key END)))
              INTO v_changes
              FROM (SELECT jsonb_object_keys(COALESCE(v_old, '{}'::jsonb)) UNION SELECT jsonb_object_keys(COALESCE(v_new, '{}'::jsonb))) AS k(key)
             WHERE k.key <> ALL (v_skip)
               AND NOT (COALESCE(v_old -> k.key, 'null'::jsonb) = 'null'::jsonb AND COALESCE(v_new -> k.key, 'null'::jsonb) = 'null'::jsonb)
               AND (TG_OP <> 'UPDATE' OR (v_old -> k.key) IS DISTINCT FROM (v_new -> k.key));

            IF TG_OP = 'UPDATE' AND v_changes IS NULL THEN
                RETURN NULL;
            END IF;

            v_label := left(COALESCE(NULLIF(btrim(v_row ->> 'display_name'), ''), NULLIF(btrim(v_row ->> 'name_en'), ''),
                                     NULLIF(btrim(v_row ->> 'legal_name_en'), ''), NULLIF(btrim(v_row ->> 'name'), ''),
                                     NULLIF(btrim(v_row ->> 'code'), ''), NULLIF(btrim(v_row ->> 'email'), '')), 200);
            v_label_ar := left(COALESCE(NULLIF(btrim(v_row ->> 'display_name_ar'), ''), NULLIF(btrim(v_row ->> 'name_ar'), ''),
                                        NULLIF(btrim(v_row ->> 'legal_name_ar'), '')), 200);

            INSERT INTO audit.entries (tenant_id, occurred_at, actor_id, actor_kind, table_schema, table_name,
                                       record_id, action, changes, transaction_id, correlation_id,
                                       record_label, record_label_ar, record_company_id, record_branch_id)
            VALUES ((v_row ->> 'tenant_id')::uuid,
                    clock_timestamp(),
                    erp.current_actor_id(),
                    COALESCE(NULLIF(current_setting('app.actor_kind', true), ''), 'unknown'),
                    TG_TABLE_SCHEMA,
                    TG_TABLE_NAME,
                    (v_row ->> 'id')::uuid,
                    lower(TG_OP),
                    COALESCE(v_changes, '{}'::jsonb),
                    txid_current(),
                    NULLIF(current_setting('app.correlation_id', true), ''),
                    v_label,
                    v_label_ar,
                    (v_row ->> 'company_id')::uuid,
                    (v_row ->> 'branch_id')::uuid);
            RETURN NULL;
        END;
        $$;
        REVOKE ALL ON FUNCTION audit.capture() FROM PUBLIC;
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

    /// <summary>
    /// Company write permission: the third, per-permission layer of row-level security (critic p02
    /// rounds 12 and 13: a manager of the Dubai LLC, read-only in the free-zone entity, closed a
    /// free-zone branch through a set-based <c>ExecuteUpdate</c> that ran before the endpoint's
    /// check and past the kernel's SaveChanges guard). A request whose permission is held only in
    /// some companies (roles assigned per company) writes company-owned rows of those companies
    /// alone: <see cref="ErpDbSession.LimitToPermissionAsync"/> sets <c>app.write_scope</c> to
    /// <c>list</c> with the companies in <c>app.write_ids</c> and the transaction's start time in
    /// <c>app.write_tx</c>, and the RESTRICTIVE <c>company_write_*</c> policies
    /// (<see cref="TenantSql.ProtectCompanyWrites"/>) refuse an insert into, and skip an update or
    /// delete of, a row of any other company, whatever statement does it. A request whose
    /// permission is held in every company, and system work, set no limit (<c>app.write_scope</c>
    /// is empty, set so by <see cref="ErpDbSession.BeginAsync"/>): the company scope alone applies.
    /// A limit whose marker is not this transaction's (left at session level) refuses every write
    /// (fail closed).
    /// </summary>
    public const string CompanyWrite = """
        CREATE OR REPLACE FUNCTION erp.company_writable(p_company_id uuid) RETURNS boolean
            LANGUAGE sql STABLE PARALLEL SAFE
            AS $$
                SELECT CASE WHEN coalesce(current_setting('app.write_scope', true), '') <> 'list' THEN true
                            ELSE coalesce(current_setting('app.write_tx', true) = extract(epoch from now())::text
                                          AND p_company_id = ANY (string_to_array(NULLIF(current_setting('app.write_ids', true), ''), ',')::uuid[]), false)
                       END
            $$;

        GRANT EXECUTE ON FUNCTION erp.company_writable(uuid) TO erp_app;
        """;

    public const string CompanyWriteDown = """
        DROP FUNCTION IF EXISTS erp.company_writable(uuid);
        """;

    /// <summary>
    /// Trigram matching for list search (ILIKE '%word%' served by GIN indexes with
    /// <c>gin_trgm_ops</c>). pg_trgm is a trusted extension shipped with PostgreSQL (PostgreSQL
    /// licence): the database owner installs it without superuser rights. It lives in
    /// <c>public</c>, which the application role may use but not create in; it adds operators and
    /// functions only, no tables and no SECURITY DEFINER code.
    /// </summary>
    public const string SearchExtension = """
        CREATE EXTENSION IF NOT EXISTS pg_trgm SCHEMA public;
        """;

    public const string SearchExtensionDown = """
        DROP EXTENSION IF EXISTS pg_trgm;
        """;

    /// <summary>
    /// Row-level security evaluates a query's own conditions after the tenant policy unless they
    /// are LEAKPROOF, and only leakproof conditions may drive an index. Case-insensitive LIKE
    /// (<c>texticlike</c>, the <c>~~*</c> operator behind ILIKE) is not marked leakproof by
    /// PostgreSQL, so under row-level security a list search could never use its trigram index and
    /// scanned every row of the tenant. texticlike has no side effects and its only errors concern
    /// the pattern (a trailing escape character), never the row value it is matched against; the
    /// platform always passes patterns as parameters, never from a row. Marking it leakproof lets
    /// the trigram index serve ILIKE under row-level security. Set by the superuser bootstrap (only
    /// a superuser may); the leakproof gate keeps the set of leakproof functions that differ from
    /// PostgreSQL's defaults reviewed.
    /// </summary>
    public const string SearchLeakproof = """
        ALTER FUNCTION pg_catalog.texticlike(text, text) LEAKPROOF;
        """;

    /// <summary>The functions besides <c>pg_stat_get_*</c> that tell how big a table, an index or
    /// the database is (all tenants' rows together).</summary>
    public static readonly IReadOnlyList<string> SizeFunctions =
        ["pg_relation_size", "pg_total_relation_size", "pg_table_size", "pg_indexes_size", "pg_database_size", "pg_tablespace_size"];

    /// <summary>
    /// PostgreSQL's statistics describe whole tables, every tenant's rows together: the live and
    /// dead row counts and activity of each table and index (<c>pg_stat_*</c>, <c>pg_statio_*</c>),
    /// other sessions and their query text (<c>pg_stat_activity</c>), sampled column values and
    /// their frequencies (<c>pg_stats</c>, <c>pg_stats_ext</c>), the <c>pg_stat_get_*</c> functions
    /// behind the views and the size functions. PUBLIC may read all of them by default, so the
    /// application role could learn other tenants' volumes and values without touching a tenant
    /// row (critic p00 round 8, plant L3). The superuser bootstrap takes them from PUBLIC in the
    /// product's database; the superuser keeps them. <c>pg_class</c> itself stays readable (every
    /// client library reads it), but only column by column: PUBLIC loses its row and page estimates
    /// (<c>reltuples</c>, <c>relpages</c>, <c>relallvisible</c> and, where the server has it,
    /// <c>relallfrozen</c>), which count every tenant's rows (critic p00 round 9); every other column
    /// is granted back. Product code may not name them either (a static gate).
    /// </summary>
    public const string RevokeCrossTenantStatistics = """
        DO $$
        DECLARE item record;
        BEGIN
          FOR item IN
            SELECT c.oid::regclass AS name
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = 'pg_catalog' AND c.relkind IN ('v', 'm', 'r')
               AND (c.relname LIKE 'pg\_stat%' OR c.relname LIKE 'pg\_statio%')
          LOOP
            EXECUTE format('REVOKE ALL ON %s FROM PUBLIC', item.name);
          END LOOP;
          FOR item IN
            SELECT p.oid::regprocedure AS name
              FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE n.nspname = 'pg_catalog'
               AND (p.proname LIKE 'pg\_stat\_get%' OR p.proname IN ('pg_relation_size', 'pg_total_relation_size', 'pg_table_size', 'pg_indexes_size', 'pg_database_size', 'pg_tablespace_size'))
          LOOP
            EXECUTE format('REVOKE EXECUTE ON FUNCTION %s FROM PUBLIC', item.name);
          END LOOP;
          REVOKE SELECT ON pg_catalog.pg_class FROM PUBLIC;
          EXECUTE (SELECT format('GRANT SELECT (%s) ON pg_catalog.pg_class TO PUBLIC', string_agg(quote_ident(a.attname), ', ' ORDER BY a.attnum))
                     FROM pg_attribute a
                    WHERE a.attrelid = 'pg_catalog.pg_class'::regclass AND a.attnum > 0 AND NOT a.attisdropped
                      AND a.attname NOT IN ('reltuples', 'relpages', 'relallvisible', 'relallfrozen'));
        END $$;
        """;

    /// <summary>The columns of <c>pg_class</c> PUBLIC may not read (see
    /// <see cref="RevokeCrossTenantStatistics"/>).</summary>
    public static readonly IReadOnlyList<string> PgClassEstimateColumns = ["reltuples", "relpages", "relallvisible", "relallfrozen"];
}
