# p00: the cost of one request, round 16

Date: 2026-11-02 (round 16, routed by the lead). Piece: p00-foundation. Scope: make each request the
gates send cheaper in processor time without changing what they send or check, and add one
measurement (needs-human #35). No sampling, no dropped endpoint, parameter, attacker, carrier or
oracle, no ratchet minimum lowered, no time-out, re-run or skip.

## Where one request's processor time went (measured on the integration branch 43253543)

Method: `RequestCostTests` (new, below) signs in as tenant A's administrator and sends 2,000 to 8,000
GETs of three endpoints one after the other through the in-process test host, with the verify stage's
runtime settings (`DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0`, `DOTNET_GCgen0size=0x4000000`,
`DOTNET_TieredPGO=0`, `ERP_TEST_PASSWORD_ITERATIONS=10000`); statements are counted from the data
source's own trace (the gates' `StatementCapture`), processor time is the process's
(`TotalProcessorTime`: client, host and driver together), and `perf` with the .NET perf map gave the
profile.

The statements of a signed-in GET, in order, before this round (each line is one round trip):

1. `identity.resolve_session(@hash)` on the unbound connection
2. `BEGIN` (driver), then `SET TRANSACTION READ ONLY` + the `set_config(...)` tenant/actor binding (one batch)
3. the user with every role (`SessionGrants.Statement`)
4. the workspace status (`TenantDirectory.Statement`)
5. the user's company access (`CompanyScopeBinder.AccessStatement`)
6. the company settings + the companies/branches/workplace query (one batch)
7. the endpoint's own statements (3 for `/api/identity/roles`: two counts and the page)
8. `COMMIT` (driver)

So nine statements before the endpoint's own for an endpoint that reads one table is the "nine SQL
statements" of the round 15 record: five round trips only to start the session (items 1 to 6 minus the
two that were already batches), each with the driver's write, a socket wake-up, a thread-pool hop and
the reader's state machine. The profile is flat (no single function above 2 %): the cost is the sum of
round trips, async continuations and per-request service resolution (DI scope, EF `DbContext`
start-up about 6 %), plus the test host's own watching (`StatementCapture.CallerOf`, a stack walk for
each settings statement, about 5 % of a request in the gate processes; kept: it is how the gate names
the code that sent a tenant setting). Variations of the runtime (concurrent GC off, tiered
compilation off, lower call-count threshold) moved nothing outside the run-to-run spread of 10 %.

## What changed

**One round trip for the session's reads.** `ErpDbSession.BeginAsync` takes an optional list of
`ISessionPrefetch` (kernel interface: a statement for the tenant and user, and a reader for its
rows). The session resolver passes the three reads every authenticated request makes (the user with
every role: Identity `SessionGrants`; the workspace status: Tenancy `TenantDirectory`; the user's
company access: Tenancy `CompanyScopeBinder`), and they go to PostgreSQL in the same batch as the
read-only mode and the tenant settings. A batch runs its statements in order, so each read runs after
the tenant is bound, in the same transaction and under the same row-level security as before; the
statements and their `@tenant` second layer are unchanged. Three round trips fewer per request (the
session start is now: lookup, binding with the reads, company scope with the workplace).

Why this is safe, point by point (the lead's conditions):

- Row-level security stays the enforcement layer, the tenant is bound per transaction by the same
  `set_config`, still through `ErpDbSession.BeginAsync` (the allowlisted single binding site, same
  `TenantBinding.Check`).
- Nothing is cached across requests. The prefetched rows live in scoped services of the request's
  unit of work and are handed out once (`TenantDirectory` and `CompanyScopeBinder` forget them on
  first use; `SessionGrants` already held them for the unit of work). A revoked session
  (`resolve_session` is still the first statement), a removed role, a changed company scope or a
  suspended workspace is read afresh by the next request, exactly as before; the existing
  identity, tenancy and G2 grant tests (revocation, removed permission, company scope change) pass
  unchanged.
- When the unit of work is already bound (sign-in, jobs, seeding, a second `BeginAsync`) nothing is
  prefetched and each consumer reads for itself, as before.
- The audit trail is unchanged (the audit context is part of the binding statement, untouched).

Changes in other pieces' modules, for their owners: p03 (Identity) `SessionGrants` also implements
`ISessionPrefetch` and `SessionResolver` passes the prefetch list; p02 (Tenancy) `TenantDirectory`
and `CompanyScopeBinder` implement `ISessionPrefetch` and are registered as themselves (scoped) plus
as `ISessionPrefetch`. Their statements are the same text as before. A module that adds another read
every authenticated request makes should do the same instead of sending it on its own
(`RequestCostTests` fails when a path makes more round trips than its ceiling).

**A measured ceiling.** `RequestCostTests` (Erp.Gates.Tests, runs alone) counts round trips and
statements per signed-in GET of three endpoints and fails above a ceiling per path (6, 5 and 7 round
trips; 12 statements); it writes processor time per request to `ERP_REQUEST_COST_FILE` when set. A new
module that puts a lookup on every request is caught in minutes, not by the processor-time maximum
of a two-hour verify.

**The machine's busy processor time** (needs-human #35): `./erp verify` records `/proc/stat`'s busy
ticks (user + nice + system + irq + softirq + steal; idle and iowait excluded) at the start and the
end (`build/verify-processes.sh`: `machine_busy_ticks`, `machine_cpu_start`, `machine_cpu_end`),
writes the seconds to `cpu-machine` beside `cpu-<stage>`, and `build/ratchet-check.mjs` prints the
machine's time, the stages' and the difference with its share. Report only. Tests:
`build/verify-processes.test.mjs` (parsing of the line, the older kernel's line, bad lines, the live
file, the seconds with two decimals, the report). `suite.buildTests` raised 6 to 13,
`suite.dotnetTests` 864 to 900 (this tree's count).

## Measured

Per request (RequestCostTests, nothing else running, processor microseconds per request, two runs
each, before / after; the spread of one configuration is about 10 %):

| Path | Round trips | Statements | CPU before | CPU after |
|---|---|---|---|---|
| `/api/identity/roles` | 9 / 6 | 11 / 11 | 5,015 and 5,178 | 3,480 and 3,533 (-31 %) |
| `/api/auth/session` | 8 / 5 | 10 / 10 | 2,794 and 2,994 | 2,338 and 2,296 (-19 %) |
| `/api/tenancy/companies` | 10 / 7 | 12 / 12 | 3,714 and 3,977 | 3,351 and 2,880 (-19 %) |

(The statement count is unchanged: the same statements run, in fewer round trips.)

Per test class, G1HttpIsolationTests alone on the host (`/usr/bin/time`, user + system, the verify
stage's runtime settings, built Release, the same machine, other builders working beside both runs at
load 7 to 25, so absolute numbers carry that noise):

| | CPU s | wall s | peak memory |
|---|---|---|---|
| integration branch 43253543 | 3,585 (user 2,830, system 754) | 4,064 | 8.9 GB |
| this branch | 2,825 (user 2,230, system 595) | 3,033 | 14.1 GB |

That is **-21 % processor time** for the largest test (the same requests: the attack code is
unchanged). The peak memory is higher because the same requests are sent faster and the SQL trace's
queues fill faster than they are drained; the process stays well inside the machine.

Whole `./erp verify` of this branch, run while another agent's verify and builders shared the
machine: every test passed (dotnet 900, web 646, e2e 133, compare 487, build 13); processor time
13,465 s (dotnet 9,915, web 3,044, e2e 464, timing 43) against the maximum of 10,600; the new
measurement showed why: the machine was busy 61,759 s over the 7,299 s run, 48,294 s (78 %) of it
other work beside the verify (load average 22.5 on average, 73 at the highest), which inflates every
stage's processor time (hyper-threads and spinning). That run is not a measurement of the branch;
the integrator's alone run is.

**The aim of 30 % of the dotnet stage was not reached**: the requests are about 70 % of the stage
and they are 20 to 30 % cheaper, so the stage is expected about 15 % cheaper (the alone run will
say). The cost per unit a new module adds follows the request: a GET endpoint without a route is
about 14,000 requests, so about 40 s of processor time before and about 32 s after.

## What was not done, and why

- Sharing application starts between gate classes (round 15 estimate 500 CPU s): every class mutates
  its environment or reads a count another class changes; not attempted in a short round.
- Folding the company binding into the first batch: its `app.company_ids` setting is the output of
  the company access read, and the gates (rightly) refuse a computed setting value; it stays a second
  round trip.
- A cheaper `StatementCapture.CallerOf`: every way found (caching by statement text, sampling) lets a
  caller that copies a reviewed statement's text pass as the reviewed code. Kept.
- Runtime switches (concurrent GC, tiered compilation): no effect outside the noise.
