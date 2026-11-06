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

## Round 4 amendment: the worker holds no owner credentials

The first choice above gave the worker `ConnectionStrings__Owner` for `SET LOCAL ROLE erp_auth`. A process that runs every
module's handlers must not hold credentials that could `ALTER TABLE ... NO FORCE ROW LEVEL SECURITY`.

Tried and rejected: a SECURITY DEFINER function `jobs.due_tenants()` owned by `erp_auth`, called on the application role (the
pattern of the sign-in lookups). It needs a third entry under the ratchet maximum `g1.securityDefinerFunctions` (2, owner's and
lead's to move) and a worker kind of caller in the reviewed-callers gate, whose HTTP attack requires every reviewed caller to be
seen in a request trace; neither belongs to this piece.

Chosen: a login role of the worker's own, `erp_worker` (`DatabaseRoles.Worker`, created by the bootstrap step beside the others,
NOLOGIN until `ConnectionStrings:Worker` gives it a password; no superuser, no BYPASSRLS, no CREATEROLE, owns nothing, member of
nothing). The jobs migration `WorkerLookupRole` moves the lookup's column grants (four of `jobs.jobs`, three of `jobs.schedules`)
and SELECT-only `due_tenants_read` policies from `erp_auth` to `erp_worker` and withdraws them from `erp_auth`. The worker
(compose service `worker`) gets `ConnectionStrings__App` and `ConnectionStrings__Worker`; the web application gets neither the
worker string nor an owner string, and cannot `SET ROLE` to it. The shared G1 policy gate now accepts a read-only policy for
`erp_worker` as well as `erp_auth` (still only if listed in `tests/Gates/policy-allowlist.txt`). Tests:
`JobRunnerTests.The_worker_holds_the_application_role_only...` (compose names no owner or admin string; both roles have no
privileged attribute, own no relation or schema, are members of nothing and cannot switch row-level security off) and
`The_due_workspaces_lookup_names_workspaces_with_work_and_sees_nothing_else` (the lookup role reads the seven columns only).

Cancel: `JobQueue.CancelAsync` is one `UPDATE ... WHERE id AND status IN ('queued','running')` (queued ends cancelled at once),
not a read and an optimistic save, so a progress report or heartbeat cannot make it answer 409. The worker's final write takes the
row `FOR UPDATE` and, when `cancel_requested` is set, ends the attempt as cancelled instead of succeeded (its handler writes roll
back); a cancel arriving after that lock waits, finds the job succeeded and is answered as a finished job, which the panel says in
English and Arabic ("jobs.cancel.alreadyFinished"). Tests: `Cancel_never_fails_while_the_worker_writes_progress...`,
`A_cancel_accepted_before_the_final_write...`, and the panel test. Added cost: three module tests, one web test, no wall-clock
thresholds.
