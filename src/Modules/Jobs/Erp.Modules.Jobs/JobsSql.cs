namespace Erp.Modules.Jobs;

/// <summary>
/// How the job worker learns which workspaces have work, without reading any workspace's data.
/// The worker is a separate process (<c>Erp.Host jobs work</c>), never the web application, which
/// stays on the application role and cannot read across workspaces. The worker connects with the
/// application role for everything it does inside a workspace, and for this one lookup with a login role of its
/// own, <c>erp_worker</c> (no owner credentials: the worker's roles own no table and bypass no policy), which
/// may read four columns of the jobs table and three of the schedules table through SELECT-only
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

    /// <summary>The worker's lookup, run as erp_worker: workspaces with a job due, a running job whose lease
    /// ran out or a schedule due, the longest waiting first. Tenant ids only.</summary>
    public const string DueTenants = """
        SELECT d.tenant_id
          FROM (SELECT j.tenant_id, j.run_at AS due FROM jobs.jobs j WHERE j.status = 'queued' AND j.run_at <= now()
                UNION ALL
                SELECT j.tenant_id, j.locked_until FROM jobs.jobs j WHERE j.status = 'running' AND j.locked_until < now()
                UNION ALL
                SELECT s.tenant_id, s.next_run_at FROM jobs.schedules s WHERE s.is_enabled AND s.next_run_at <= now()) d
         GROUP BY d.tenant_id
         ORDER BY min(d.due), d.tenant_id
         LIMIT @limit
        """;

    /// <summary>Moves the lookup's grants and policies from the NOLOGIN resolver role to the worker's own login role.</summary>
    public const string WorkerRoleUp = """
        DROP POLICY IF EXISTS due_tenants_read ON jobs.schedules;
        DROP POLICY IF EXISTS due_tenants_read ON jobs.jobs;
        REVOKE ALL ON jobs.schedules FROM erp_auth;
        REVOKE ALL ON jobs.jobs FROM erp_auth;
        REVOKE ALL ON SCHEMA jobs FROM erp_auth;
        GRANT USAGE ON SCHEMA jobs TO erp_worker;
        GRANT SELECT (tenant_id, status, run_at, locked_until) ON jobs.jobs TO erp_worker;
        GRANT SELECT (tenant_id, is_enabled, next_run_at) ON jobs.schedules TO erp_worker;
        CREATE POLICY due_tenants_read ON jobs.jobs AS PERMISSIVE FOR SELECT TO erp_worker USING (true);
        CREATE POLICY due_tenants_read ON jobs.schedules AS PERMISSIVE FOR SELECT TO erp_worker USING (true);
        """;

    public const string WorkerRoleDown = """
        DROP POLICY IF EXISTS due_tenants_read ON jobs.schedules;
        DROP POLICY IF EXISTS due_tenants_read ON jobs.jobs;
        REVOKE ALL ON jobs.schedules FROM erp_worker;
        REVOKE ALL ON jobs.jobs FROM erp_worker;
        REVOKE ALL ON SCHEMA jobs FROM erp_worker;
        GRANT USAGE ON SCHEMA jobs TO erp_auth;
        GRANT SELECT (tenant_id, status, run_at, locked_until) ON jobs.jobs TO erp_auth;
        GRANT SELECT (tenant_id, is_enabled, next_run_at) ON jobs.schedules TO erp_auth;
        CREATE POLICY due_tenants_read ON jobs.jobs AS PERMISSIVE FOR SELECT TO erp_auth USING (true);
        CREATE POLICY due_tenants_read ON jobs.schedules AS PERMISSIVE FOR SELECT TO erp_auth USING (true);
        """;
}
