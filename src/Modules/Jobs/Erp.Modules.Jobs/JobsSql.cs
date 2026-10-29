namespace Erp.Modules.Jobs;

/// <summary>
/// The one reviewed cross-tenant lookup of the jobs module: <c>jobs.due_tenants</c>, a SECURITY DEFINER
/// function owned by the NOLOGIN role <c>erp_auth</c>, which a worker calls (on a connection bound to
/// no workspace) to learn which workspaces have a job due, a running job whose lease ran out, or a
/// schedule due. It returns tenant ids only, never a job, a title or a payload; it reads three
/// columns of the jobs table and three of the schedules table through a SELECT-only policy, and
/// answers nothing on a connection bound to a workspace. Everything after that runs inside the
/// workspace's own row-level security. Listed in tests/Gates/security-definer-allowlist.txt,
/// security-definer-callers.txt and policy-allowlist.txt.
/// </summary>
internal static class JobsSql
{
    public const string Up = """
        GRANT USAGE, CREATE ON SCHEMA jobs TO erp_auth;
        GRANT SELECT (tenant_id, status, run_at, locked_until) ON jobs.jobs TO erp_auth;
        GRANT SELECT (tenant_id, is_enabled, next_run_at) ON jobs.schedules TO erp_auth;
        CREATE POLICY due_tenants_read ON jobs.jobs AS PERMISSIVE FOR SELECT TO erp_auth USING (true);
        CREATE POLICY due_tenants_read ON jobs.schedules AS PERMISSIVE FOR SELECT TO erp_auth USING (true);

        CREATE INDEX ix_jobs_queued_due ON jobs.jobs (run_at) WHERE status = 'queued';
        CREATE INDEX ix_jobs_running_lease ON jobs.jobs (locked_until) WHERE status = 'running';
        CREATE INDEX ix_schedules_due ON jobs.schedules (next_run_at) WHERE is_enabled;

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
             LIMIT least(greatest(p_limit, 1), 200)
        $$;

        ALTER FUNCTION jobs.due_tenants(integer) OWNER TO erp_auth;
        REVOKE ALL ON FUNCTION jobs.due_tenants(integer) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION jobs.due_tenants(integer) TO erp_app;
        REVOKE CREATE ON SCHEMA jobs FROM erp_auth;
        """;

    public const string Down = """
        DROP FUNCTION IF EXISTS jobs.due_tenants(integer);
        DROP INDEX IF EXISTS jobs.ix_schedules_due;
        DROP INDEX IF EXISTS jobs.ix_jobs_running_lease;
        DROP INDEX IF EXISTS jobs.ix_jobs_queued_due;
        DROP POLICY IF EXISTS due_tenants_read ON jobs.schedules;
        DROP POLICY IF EXISTS due_tenants_read ON jobs.jobs;
        REVOKE ALL ON SCHEMA jobs FROM erp_auth;
        """;
}
