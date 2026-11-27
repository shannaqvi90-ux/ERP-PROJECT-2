# p00 round 24: a more reliable verify (stage layout, balanced HTTP shares, migrated template)

Date: 2026-11-27. Piece: p00-foundation. Status: accepted. Routed by the lead (serial-27a): the r23 critic got 1 of 3
alone verifies green on the same commit (8,639 s wall, 26,092 s processor time in run 3). No test, gate, attack,
control, time limit or ratchet value was weakened, skipped, deleted or lengthened.

## 1. Timed tests wait for the build peaks

Cause of run 1's failure: `web/src/modules/shell/clientIsolation.test.tsx` (30 s per test) started in the first minutes
of the verify, beside the .NET build, the application image build and the clean stack's migrations and seed (load 16 to
23). Now the web stage still starts first and does its untimed work (copy, `npm ci`, type-check, string gate), then
`wait_for_builds` (`build/verify-inside.sh`) blocks until

* the dotnet stage has written `dotnet-build.done` (right after `dotnet build`), and
* `./erp` has written `stack-ready` (image built, database set up, app healthy),

or the stage that should have written its marker has ended (`dotnet.status`, `e2e.status`: a failed stage counts as
over), or 40 minutes have passed (then the tests run anyway; a hung build is not hidden). Only after that do vitest,
the plant self-tests, the web build, the verify-machinery tests, the comparison harness's unit tests and the instrument
mutations run. Wall time: the dotnet stage (about two hours) is the longest, the web stage about 20 minutes, so
waiting costs nothing; the web tests now run beside the .NET test groups (three at once) instead of beside two builds.
The timed e2e stage already waited for the end of the dotnet stage.

## 2. HTTP attack shares dealt by measured cost

Before: families were dealt to four shares by a guess (`AttackShard.Weight`); the guess put a list's report requests
and the numbering sequences at a fraction of their cost. r23 alone run 3: shares took 30, 33, 60 and 38 min and
2,308, 2,373, 4,799 and 3,182 processor-seconds.

Measured (new report in every share's output: `RequestLog.DescribeByFamily`, request seconds by endpoint family and
label; all four shares side by side, 2026-11-27, the machine at load 14 to 75 because other agents ran): tenant A's
attack seconds per family, for example `/api/numbering/sequences` 3,501 s, `/api/reports/lists/audit.log` 2,878 s,
`/api/audit/log` 1,660 s, `/api/identity/users` 1,173 s. With the old guess the four shares carried 8,593, 5,543, 9,390 and
5,725 of these seconds (walls 60, 51, 72 and 65 min under that load). `AttackShard.MeasuredFamilySeconds` now holds the 46
families at or above 180 s; the other families take their guessed weight times `GuessToSeconds` (0.16, from the list
views' families: guessed 155 s, measured 190 to 265 s). Dealt heaviest first to the lightest share as before, the four
shares carry 7,126, 7,129, 7,126 and 7,131 of the modelled seconds.

Unchanged: `AttackShard.For` is deterministic, the shares are disjoint and whole, `HttpAttackJudge` still refuses an
endpoint attacked twice or by no share, a missing or repeated share and every ratchet minimum. New test
`The_measured_families_are_dealt_so_that_no_share_is_far_above_the_average...` keeps the table balanced (max share
within 5 % of the average, no family above a fair share). The table is a measured cost, not a rule: refresh it from the
"request seconds by endpoint family" lines when the product grows; a wrong entry makes a share slower, never a request
missing. Not measured here: the shares' wall time and processor time after the change (that is the integrator's alone run).

## 3. Migrated template per test process (owner decision #59 step 3)

The gate environments were already copies of one seeded template per process and settings combination
(`GateTemplateAsync`). The other environments (`ErpTestEnvironment.StartAsync(plan)`, eight call sites: the volume tests,
`KernelDataTests`, `BulkTimeoutTests`, the seed tests) bootstrapped, migrated and seeded a new database each time. Now
`TestDatabaseServer.MigratedTemplateAsync` makes, once per process and settings combination, a bootstrapped and migrated
database without seed, closes it, and `StartAsync` copies it (`CREATE DATABASE ... TEMPLATE`), then runs the bootstrap
(privileges on the database itself are not copied), the migrator (a no-op check) and the seed with the test's own plan.
Every test still gets a database of its own, fresh and isolated, seeded as before. A template shared across the test
processes would need one server for all of them (roles are server-wide with per-process passwords); that was not done.
Saving: the migrations of every `StartAsync` after the first per process and settings combination; small (a few
environments per process), so the processor-time saving is a few minutes at most. Checked: Kernel (KernelData, BulkTimeout),
Identity (seed, users volume), Currency (whole-list seed) and Contacts volume tests pass; the contacts list-volume
budget test failed once at load 75 and passed on two other runs.

Processor time this round adds to a verify: none beyond the request-seconds counters (a dictionary update per request).

## Integrator's alone run (2026-11-27)

The first full verify of this round: the integrator's alone run (`ERP_VERIFY_ALONE=1`, both slots) of merge b2edcb19
(piece head 4c7a1f69 on 82611d1b), slot taken 11:28:56 UTC. It passed every stage and the ratchet check.

| | this run | r23 alone run 3 |
|---|---|---|
| Wall time | 8,557 s | 8,639 s |
| Verify processor time | 25,395 s (dotnet 21,547, web 2,867, e2e 878, timing 103) | 26,092 s |
| Load | 1.22 at the start, 1.54 at the end, highest 33.52 over 572 readings on 16 CPUs | |
| Tests | dotnet 1,230, web unit 813, e2e 180, compare 499, build 22; none failed or skipped | |

Stage walls: dotnet 7,172 s, web 4,845 s, timing 291 s. The web stage's timed tests started after "builds over after
waiting 25 s (load 10.00)". The machine counted 59,881 s of processor time over the run, 34,486 s of it outside the
verify stages (the ratchet's own report). One run only: the done criterion asks for 3 of 3 from fresh clones, which is
the critic's to measure. The per-share processor time (cpu-group-http-N) and the shares' family reports were in the run's
output folder, which a passing run removes, so they are not recorded here. All four http shares and the three
non-interference shares passed.
