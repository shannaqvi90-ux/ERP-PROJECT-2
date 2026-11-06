namespace Erp.Modules.Jobs;

/// <summary>
/// How the job worker learns which workspaces have work, without reading any workspace's data.
/// The worker is a separate process (<c>Erp.Host jobs work</c>) on the application role only
/// (no owner credentials). For this one lookup it calls <c>jobs.due_tenants()</c>, a SECURITY DEFINER function
/// owned by the NOLOGIN role <c>erp_auth</c>, which may read three columns of the jobs table and three of the schedules table through SELECT-only
/// policies: workspace id, state and the moment something is due. It sees no title, parameter,
/// error or result. Everything after the lookup runs under the application role bound to one
/// workspace as system work, inside that workspace's own row-level security. Listed in
/// tests/Gates/policy-allowlist.txt and tests/Gates/tenant-bypass-sources.txt.
/// </summary>
internal static class JobsSql
{
    public const string Up = """
        GRANT USAGE ON SCHEMA jobs TO erp_auth;
        GRANT SELECT (tenant_id, status, run_at, locked_until) ON jobs.jobs TO erp_auth;
        GRANT SELECT (tenant_id, is_enabled, next_run_at) ON jobs.schedules TO erp_auth;
        CREATE POLICY due_tenants_read ON jobs.jobs AS PERMISSIVE FOR SELECT TO erp_auth USING (true);
        CREATE POLICY due_tenants_read ON jobs.schedules AS PERMISSIVE FOR SELECT TO erp_auth USING (true);

        CREATE INDEX ix_jobs_queued_due ON jobs.jobs (run_at) WHERE status = 'queued';
        CREATE INDEX ix_jobs_running_lease ON jobs.jobs (locked_until) WHERE status = 'running';
        CREATE INDEX ix_schedules_due ON jobs.schedules (next_run_at) WHERE is_enabled;
        """;

    public const string Down = """
        DROP INDEX IF EXISTS jobs.ix_schedules_due;
        DROP INDEX IF EXISTS jobs.ix_jobs_running_lease;
        DROP INDEX IF EXISTS jobs.ix_jobs_queued_due;
        DROP POLICY IF EXISTS due_tenants_read ON jobs.schedules;
        DROP POLICY IF EXISTS due_tenants_read ON jobs.jobs;
        REVOKE ALL ON SCHEMA jobs FROM erp_auth;
        """;

    /// <summary>The worker's lookup body, owned by erp_auth (see <see cref="DueTenantsFunctionUp"/>): workspaces with a
    /// job due, a running job whose lease ran out or a schedule due, the longest waiting first. Tenant ids only.</summary>
    public const string DueTenants = "SELECT tenant_id FROM jobs.due_tenants(@limit)";

    /// <summary>The reviewed lookup the worker calls on the application role's connection. It is a SECURITY DEFINER
    /// function owned by the NOLOGIN role erp_auth (as the sign-in lookups are), so the worker process holds no
    /// owner credentials. It answers only on a connection not bound to a workspace and returns tenant ids only.</summary>
    public const string DueTenantsFunctionUp = """
        GRANT CREATE ON SCHEMA jobs TO erp_auth;
        SET LOCAL ROLE erp_auth;
        CREATE FUNCTION jobs.due_tenants(p_limit integer)
            RETURNS TABLE (tenant_id uuid)
            LANGUAGE sql STABLE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
        AS $$
            SELECT d.tenant_id
              FROM (SELECT j.tenant_id, j.run_at AS due FROM jobs.jobs j WHERE j.status = 'queued' AND j.run_at <= now()
                    UNION ALL
                    SELECT j.tenant_id, j.locked_until FROM jobs.jobs j WHERE j.status = 'running' AND j.locked_until < now()
                    UNION ALL
                    SELECT s.tenant_id, s.next_run_at FROM jobs.schedules s WHERE s.is_enabled AND s.next_run_at <= now()) d
             WHERE coalesce(current_setting('app.tenant_id', true), '') = ''
             GROUP BY d.tenant_id
             ORDER BY min(d.due), d.tenant_id
             LIMIT greatest(least(p_limit, 500), 0)
        $$;
        RESET ROLE;
        REVOKE CREATE ON SCHEMA jobs FROM erp_auth;
        REVOKE ALL ON FUNCTION jobs.due_tenants(integer) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION jobs.due_tenants(integer) TO erp_app;
        """;

    public const string DueTenantsFunctionDown = "DROP FUNCTION IF EXISTS jobs.due_tenants(integer);";
}
