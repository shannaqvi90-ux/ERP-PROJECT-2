# p00 — `./erp verify` fast again, without running fewer tests

Date: 2026-10-04. Piece: p00-foundation (round 5). Status: accepted.

## Context

`./erp verify` took about 3 minutes in wave 0 and 75-100 minutes after wave 1 when two or three
agents shared this 4-CPU machine (fresh-clone runs of 4,503 s and 6,004 s; the tenant-isolation
HTTP test alone 37 minutes). Integrators ran out of time. Nothing may be removed, skipped, sampled
down or weakened (CLAUDE.md rule 9), so the time had to come from how the same work runs.

## Where the time went (measured)

At the integration commit `2b4241d` (wave 1), `./erp verify` on this machine with another agent's
suite running alongside (load average 5 at the start, 10-30 later): the gate assembly alone took
**6,777 s** (113 min) and the run was stopped at 7,200 s while still building the end-to-end stack.
The other four test assemblies took 30-100 s each, the web stage 93 s, the comparison harness 111 s.
Inside the gate assembly (TRX durations):

| Test | Time |
|---|---|
| HTTP attack self-test (planted module) | 5,240 s |
| G1 HTTP attack | 3,381 s |
| company attack self-test | 1,173 s |
| G1 company attack | 663 s |
| non-interference self-test | 245 s |
| G1 non-interference | 147 s |

The planted-module self-tests share static planted state, so they ran one after another in one
collection: 6,700 s of serial work, the whole suite's critical path. And every attack sent its
requests one at a time except phase 2.

Per request (a signed-in `GET`, measured with an activity listener): 11-13 statements, each a
round trip through Docker's userland port proxy (0.25 ms against 0.12 ms to the container's own
address, measured), and every statement planned afresh — a statement under the row-level security
policies took 2.6-4.8 ms to plan and 0.1-0.4 ms to run (`EXPLAIN ANALYZE` as `erp_app`).
Profiling the gate process showed it mostly waiting, using 1-1.5 of the 4 CPUs.

## Decision

Nothing runs less; everything runs side by side or cheaper.

1. **Stages side by side** (`erp`, `build/verify-inside.sh`). Three stages start together: the
   .NET build and tests; the web app (type-check, string gate, unit tests, plant self-tests, build)
   with the comparison harness's tests; and the clean stack (image build, migrations, seed). The
   end-to-end tests start when the stack is up and the .NET stage is over (a browser with timeouts
   is not run against the suite's heaviest load). Then the timing budgets, still alone (on the
   .NET stage's build, kept in a volume of the run), then the ratchet. Each stage's output is
   prefixed with its name and kept whole; a failed stage is named with the end of its log.
2. **Test processes side by side.** The three long planted-module self-tests (HTTP attack, company
   attack, non-interference) each run in a `dotnet test` process of their own (trait `Process`),
   next to the process running every other test. Their planted state is static, so separate
   processes keep it apart; in a plain `dotnet test` they still take turns in their collection.
   Every test runs exactly once (the filters partition the suite; the ratchet counts every TRX).
3. **The longest collections start first** (`HeavyCollectionsFirst`, an xUnit collection
   orderer): with four collections at a time, a long attack starting last ran alone at the end.
4. **Attacks send four requests at a time where order does not matter.** G1 phase 1 (routes,
   tenant headers, guessed queries) and phase 3 (tenant B values in body fields) now build each
   endpoint's (or field's) requests in the same order and with the same values as before, then send
   them four at a time — still after tenant B's touches of that endpoint and before the ones after
   it; sign-in and sign-out, which change the attackers' own sessions, stay one at a time (as the
   tenant-switch phase already did). Phase 3 judges a field's answers once all of them are in,
   after the values its successful writes stored count as the attacker's own (concurrent writes
   to the same record). The company attack does the same per endpoint and attacker: reads judged
   as they come (with their controls), writes judged after the batch. The non-interference
   comparisons and write-after-write pairs stay strictly sequential: their order is the test.
5. **One PostgreSQL per test process, gate environments copied from a template.**
   `TestDatabaseServer` starts one server per test process (instead of one per test class); each
   environment gets a database of its own. The first gate environment of each settings combination
   is bootstrapped, migrated and seeded by the product's own code into a template database; every
   other one is `CREATE DATABASE … TEMPLATE` (milliseconds) and then runs the product's bootstrap
   (database privileges are not copied) and migrator (no pending migration, the security
   invariants) before use. Environments of an explicit seed plan (demo volume, shared dataset,
   slow seed) are seeded in their own database as before. Bootstraps take turns: the roles are
   server-wide and PostgreSQL refuses two sessions creating or altering one role at once.
6. **No port proxy.** `./erp verify` runs the tests on the Docker host's network, where the test
   servers' own addresses are reachable; `ERP_TEST_DB_DIRECT=1` makes the tests connect there.
   Elsewhere (a developer's laptop) they keep the published port.
7. **Statements prepared once per connection (product).** The application role's pools prepare a
   statement once it has run twice on a connection (`Max Auto Prepare` 256, min usages 2), so the
   session lookup, tenant and company binding and permission checks are planned once per pooled
   connection instead of on every request. This is production behaviour, not a test switch: the
   demo and production get the same saving.
8. **Cheaper judging.** The attack's marker search is one multi-string pass
   (`SearchValues<string>`, case-insensitive) over each answer instead of one scan per marker, and
   the markers are computed once per snapshot instead of on every answer. The SQL trace works out
   which code sent a statement (a stack walk) only for statements that change settings.

## Measurements

| Run | Machine | Result |
|---|---|---|
| `./erp verify`, `2b4241d` (before) | 2-3 agents, load 5→30 | gate assembly 6,777 s; stopped at 7,200 s, before end-to-end |
| lead's fresh-clone runs (before) | 2-3 agents | 4,503 s and 6,004 s |
| all .NET tests, this round, 4 processes | load 14-53 (two other suites) | 2,735 s (every test passed: suite 2,312 s, HTTP self-test 2,735 s, company self-test 2,265 s, non-interference self-test 582 s) |
| G1 company attack alone | load about 9 | 11 min → 3 min |
| one signed-in GET (bench, 200 in a row) | load 7-15 | 13-21 ms → 8-14 ms |
| `./erp verify`, this round | see below | see below |

VERIFY_RESULTS

## Ratchet

`gauntlet/ratchet.json` gains the maximum `verify.quietSeconds`: `./erp verify` writes its wall
time (before the ratchet step) and the machine's one-minute load average and CPU count when it
started; `build/ratchet-check.mjs` fails the run when it started on a quiet machine (load at most
0.5 per CPU) and took longer than the maximum. A run that started on a busy machine is reported,
not judged: other agents' work decides its time. Like every maximum it may only go down.

## Why not

- **Fewer requests, sampled endpoints or a smaller attack**: rule 9.
- **One environment for many test classes**: tests that write would see each other's data; the
  template makes a fresh database per class cheap instead.
- **Running the end-to-end tests during the .NET stage**: their timeouts would decide them on a
  saturated machine.
- **node_modules cached between runs**: `npm ci` takes seconds off the critical path now that the
  web stage runs beside the .NET stage, and a shared cache volume written by two runs at once
  could corrupt.
