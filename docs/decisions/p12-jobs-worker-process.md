# p12 jobs: the worker is its own process and finds work through a narrow role

Piece p12 (round 1). Status: built.

## The problem

Jobs of every workspace sit in one table under forced row-level security. A worker must learn which
workspaces have a job due without being able to read any workspace's data. The web application cannot
(its role is `erp_app`: no BYPASSRLS, and the platform refuses to start the web app on any other role).

## Options weighed

1. A SECURITY DEFINER lookup (`jobs.due_tenants()`), the pattern identity uses for sign-in. Built first
   and dropped: the G1 reviewed-lookup gate allows only `authentication` and `endpoint:<name>` callers and
   demands that every reviewed caller is seen in the HTTP attack's trace; a background worker is neither.
   Making it fit means changing p00's shared gate machinery, which this piece may not do.
2. In-memory wake-ups (tenants learned from requests). Not durable: after a restart, due jobs of a
   workspace nobody touches would wait.
3. A superuser connection in the worker (as the operator `tenant` command uses). Works, but a long-running
   process holding the administrator role is the widest possible grant for a three-column lookup.
4. **Chosen.** The worker is a separate process, `Erp.Host jobs work` (compose service `worker`, same
   image). It holds the owner role (`erp_owner`: no BYPASSRLS, row-level security forced on it too) and,
   for the one lookup, runs `SET LOCAL ROLE erp_auth` (the NOLOGIN role the bootstrap already lets the
   owner switch to and nobody else). `erp_auth` was granted, by the jobs migration, `SELECT` on four
   columns of `jobs.jobs` (`tenant_id, status, run_at, locked_until`) and three of `jobs.schedules`
   (`tenant_id, is_enabled, next_run_at`) through SELECT-only policies (`due_tenants_read`). The lookup
   returns workspace ids, nothing else. Every other statement of the worker runs on the application
   role, bound to one workspace as system work.

## Consequences

- The web application holds no new power. The owner role lives in the worker process only; the
  `tenant-bypass-sources` gate lists the file that reads `ConnectionStrings:Owner` and creates the data
  source, with the reason.
- Tests: `JobRunnerTests` prove the lookup role reads only those columns (title, parameters, results,
  errors, ids and the tries table are all `42501`), that the application role cannot `SET ROLE erp_auth`,
  and that the worker loop runs jobs and stops without losing a running one.
- `./erp up` starts the `worker` service beside `app`; without it jobs queue and wait. In the Testing
  environment nothing runs jobs by itself: tests drive `JobRunner` directly.
- A gate for p00 to consider (not done here): a first-class `worker:<name>` caller kind for reviewed
  cross-tenant lookups, which would let this lookup be a definer function inside the web process.
