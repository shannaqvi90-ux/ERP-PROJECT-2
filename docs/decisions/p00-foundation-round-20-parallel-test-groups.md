# p00 round 20: the dotnet stage runs its test groups side by side, each with a database of its own

Date: 2026-11-16 (round 20, started early by the lead outside a workflow, at the owner's request).
Piece: p00-foundation. Status: accepted, measurement in the last section.

## Why

Owner decision #50 (2026-11-15, `gauntlet/goal.md`): p00 may cut the alone integration verify's wall
time by running test groups in parallel, each with its own database (in-memory databases allowed),
without removing or weakening any test or gate; integration still runs alone and one at a time; if
parallel running changes `verify.cpuSeconds`, the numbers go to the owner before any ratchet change.
Integration verifies took 12,500 to 13,000 s of wall time, and two pieces of serial-21 were turned back
on processor time alone, which the second goal (steadier processor time) is about.

## Where the wall time was

From the round-19 critic's alone run (`verify-critic-p00-foundation-r19.log`): stage dotnet 10,374 s of
a 11,656 s verify, and in it the single test `G1HttpIsolationTests.Tenant_A_cannot_reach_tenant_B_through_any_endpoint`
took 2 h 40 min (9,600 s). The next longest tests were G1NonInterferenceTests (55 min), the HTTP
attack's self-test (21 min) and G1ListVolumeTests (20 min); everything else in the stage is minutes. So
running the existing test classes in more processes could not shorten the stage: the stage was one
test. The HTTP attack itself had to be cut.

Where that one test spends its time (one fourth of the endpoints, measured on this round's first run
of share 1, 3,989 s, at machine load 20 to 70, tenant A 399,000 requests, tenant B 461,000):

| Phase of the attack | Seconds | What it is |
|---|---|---|
| 1, route values, tenant headers, guessed queries | 267 | per endpoint |
| 1b, tenant switch inputs | 57 | per endpoint |
| 1c, write after write | 1,456 | 33 write endpoints, 44 s each: after each variant both tenants read every GET |
| 1d, answer shapes | 38 | 5 endpoints |
| 2, parameters, 2b lists through their query contract | 470 | per endpoint, per list |
| 2c, list answers judged against each tenant's own rows | 1,343 | 3 lists, 450 s each (four runs, both tenants, every row walked) |
| 3, tenant B's values in body fields | 60 | per body endpoint |

Phases 1c and 2c are 70 % of it, and both are cut by the endpoint and by the list, so an endpoint cut
shortens them.

## Decision 1: the attack is cut by endpoint into four shares, each its own test, process and database

`IsolationAttack.RunAsync` takes an optional `AttackShard` (G1/HttpAttackShards.cs). Tenant A attacks
the endpoints of its share, in every phase, with every attacker, value and control the whole attack
uses. Tenant B uses **every** endpoint in every share: its warm-up writes, its reads before the attack,
its touches, its concurrent reader, its answers judged for tenant A's markers. So what the process
holds when tenant A attacks is the same whichever share it is: a leak that tenant B fills through one
endpoint and tenant A reads through another is caught in the share that attacks the reading endpoint.
(The planted-leak self-test below proves it with the fill and the read in different shares.)

Cutting the attack showed three things the whole attack had done by order alone; each is made explicit,
none changes a request the attack sent:

1. **Tenant A's prerequisites.** In the whole attack tenant A's write pairs came after tenant A's
   earlier phases and found, for a write that saves or runs a record again, a record an earlier
   endpoint's write had made (a saved view, a job). A share's write pairs cover its own write endpoints
   only, so before them tenant A makes every write endpoint once, creates first, the way tenant B's
   warm-up does (`TenantActivity.WriteAsync`), and every one must succeed, as tenant B's must. First run
   without it: 354 of tenant A's writes answered 404 (no saved view, no job to save) and the
   differential oracle fired on `GET /api/audit/log?sort=action` (a value tenant A holds itself once
   its saved view exists; the oracle skips such values).
2. **Endpoints that depend on each other's pairs stay in one share.** The whole attack passed the write
   pair of `POST /api/jobs/jobs/{id}/rerun` only because the pair of `.../cancel`, which the inventory
   lists before it, had cancelled the queued job both tenants then re-ran (a job that has not finished
   cannot be run again, 409 `jobs.notFinished`, and the test host runs no job worker, so a queued job
   never finishes). With cancel and rerun in different shares, both tenants' rerun answered 409 (found
   first on the first full run of the share that owned rerun, reproduced in 3 minutes with the jobs
   endpoints alone). A share therefore takes whole **families** of endpoints (`AttackShard.FamilyOf`:
   the path before the first route parameter: a job's list, create, route, cancel, rerun and output;
   a list's saved views), in the inventory's order, the order the pairs depended on. A wait for the jobs
   to finish was tried first and removed: nothing finishes, so it waited half an hour for nothing.
3. **Probes.** Every module's isolation probe runs in exactly one share (probe number modulo the share
   count); every share checks that every surface kind has a probe (`uncovered`), as the whole attack did.

The families are dealt out by weight, heaviest first to the lightest share (`AttackShard.For`),
the same in every process from the same inventory. Weights are tenths of a second calibrated on the
first run: write endpoint 450, list +4,000, GET without a route 60, with a route 25. A bad guess makes
one share slower, never a request missing: the judge below checks that the shares are the whole
inventory.

**The verdict** is `G1HttpIsolationTests.Tenant_A_cannot_reach_tenant_B_through_any_endpoint` (same name
and class as before), now the judge (`HttpAttackJudge`): it reads the four shares' results and applies
every check the single test applied. Lists that must be empty (leaks, oracles, lookup misuse, changed
tables, server errors, uncovered surfaces, untraced functions, bind, setting and tenant-value violations,
writable reads, trace blind spots, unobserved statements, input enumerations, environment changes, whole
table reads, tenant B's blind spots, unsuccessful writes of either tenant, write-pair, Arabic and list
blind spots, wrong list answers, list refusals, process state changes) are the shares' lines put
together: nothing may be found in any share. Numbers against `gauntlet/ratchet.json` minimums are the
shares' numbers added up, except three that every share counts the same way (`VictimValues`,
`VictimWriteEndpoints`, `StateLinesFingerprinted`, tenant B's own work and the process's own state),
which every share must reach by itself, and the two that are sets (header names and documented enum
values), which are counted as the union. The judge also fails when a share left no result, when the
shares disagree on the endpoint inventory, when an endpoint is attacked by two shares or by none. No
minimum is changed; `Ratchet.Min` is read as before. Each share's own test (`G1HttpIsolationShardTests`,
trait `Shard`) fails on anything its share found, so a leak fails its share's process at once.

A plain `dotnet test` (no `ERP_GATE_SHARD_DIR`) still works as one test: the judge runs a share nobody
ran yet in its own process, one after another, each in an environment of its own, and the shares'
tests reuse the result. A critic that plants a fault and runs
`--filter FullyQualifiedName~G1HttpIsolationTests` gets the whole attack, as before, only slower by the
shares' set-up. In `./erp verify` `ERP_GATE_SHARD_DIR` is set and the judge never runs a share itself.

## Decision 2: the dotnet stage runs its groups in separate processes

`build/verify-inside.sh dotnet` now starts these test processes side by side, each with its own
Testcontainers PostgreSQL server (the test library has always started one server per process) and its
own databases, longest first: `http-0` to `http-3` (the shares), `noninterference`
(G1NonInterferenceTests), the three planted-module self-tests as before (`self-http`, `self-company`,
`self-noninterference`), `listvolume` (G1ListVolumeTests), `gates-rest` (every
other gate test) and `projects` (every other test project). After they have all ended `http-judge` runs.
Row-level security and tenant isolation tests run against real PostgreSQL with row-level security in
every group; nothing is in memory.

**Every test still runs exactly once.** Before anything runs, the stage lists the tests of the whole
solution and of every group (`dotnet test --list-tests`, 5 s each) and fails if the groups are not the
whole list exactly (`tests-all.txt` against `tests-groups.txt` in the kept folder). Checked on this
branch: 842 test names, each in one group (the 842 include the shares, the judge and the new tests).

Reports: one TRX per group in `trx/` (`http-0_...trx`, `projects_...trx`, ...), read by
`build/ratchet-check.mjs` as the four before; `dotnet-<group>.log` per group and the whole in
`dotnet.log`. The old `dotnet-suite.log` no longer exists: the HTTP attack's output is in
`dotnet-http-<k>.log`, the gate tests' in `dotnet-gates-rest.log`, the other projects' in
`dotnet-projects.log`.

## Self-tests (planted faults)

- `HttpAttackShardTests` (15 tests, no database): the shares are disjoint, together every endpoint,
  the same whatever order the inventory comes in, and balanced; every counter of the report is either
  added up or named as holding in every share; the judge passes shares that are the whole attack and
  catches each planted fault: a share missing, a count that differs, an endpoint attacked twice, an
  endpoint attacked by none, shares with different inventories, a finding in any one of nine kinds of
  list in any one share, a number below its ratchet minimum when added up, one share below the minimum of
  a number every share must reach, header names below the minimum, workspace writes that do not equal
  their refusals.
- `ShardedAttackSelfTests` (new; in `gates-rest`, where it takes turns with the other planted-module tests): the planted module's leaks, filled through an
  endpoint of one share and read through an endpoint of the other, each share in an environment of its
  own, are all found: the captured array handed to the next writer in both directions, the Arabic-only
  greeting, the static cache, the singleton holding the last list, the tenant header and the route id.
- Unchanged and still run unsharded in their own process: `GateSelfTests` (every plant of the HTTP,
  company and non-interference attacks).

## Coverage, gate by gate

| Gate | Before | Now |
|---|---|---|
| G1HttpIsolation (the attack) | one test, every endpoint | four shares, each endpoint attacked in exactly one share with every request of the whole attack; tenant B in every share uses every endpoint; the judge checks every check and every ratchet minimum on the shares together |
| Differences | | (a) the header, query and cookie names the tenant switch phase learns from what the running app read (`RequestInputRecorder`) are those read by tenant B's use of every endpoint and by the share's own requests, not by the other shares' requests; the names found in the app's source and the guessed names are the same in every share; the judge counts the union. (b) tenant A's writes in the other shares' pairs are replaced, in a share, by one write of every endpoint before its pairs. (c) tenant B's concurrent reader runs while its share's phases 2 to 3 run |
| G1NonInterference, G1ListVolume, G1CompanyIsolation, G1ShellAnswers, G2*, source gates | each as before | unchanged, in the group processes above |
| GateSelfTests, NonInterferenceSelfTests | each as before, three processes | unchanged |

Nothing is removed, narrowed, sampled down, retried or given a longer timeout, and no wait is added.

## Not done

- `G1NonInterferenceTests` (55 min) is the next longest test and now the longest of the stage after the
  shares. It has the same shape (both tenants write everything first, then compare each endpoint) and could
  be cut by endpoint the same way for another 10 to 15 minutes of wall time. Not done: the gain is small
  against the risk, and it needs the same set of fixes the HTTP attack needed.
- Steadier processor time: see the last section.

## Measured

All numbers are from this machine (16 threads, 64 GB), the dotnet stage alone.

**Before**: the round-19 critic's alone verify of 494c5e27 (`ERP_VERIFY_ALONE=1`, load 0.1 at the start;
`verify-critic-p00-foundation-r19.log` and `ratchet-check.txt`): stage dotnet 10,374 s wall, 16,534 s
of processor time (the toolbox container, what `verify.cpuSeconds` counts); the whole verify 11,655 s
wall and 19,648 s (dotnet 16,534, web 2,346, e2e 646, timing 122); the machine's busy time over the
run 55,057 s, of which 64.3 % was other work beside the verify (the PostgreSQL containers).

**After**: `build/verify-inside.sh dotnet` of this branch (4f22209d, the commit before the last fixes
below), run as `./erp` runs it (same toolbox image, volumes and variables) through
`ERP_VERIFY_ALONE=1 verify-slot.sh`; slot taken 2026-11-16T17:43:12Z, load 3.7 at the start, mean 37.6
and highest 99 over 325 readings (16 threads), at most 22 GB of memory in use.

| | Before (r19, alone) | After |
|---|---|---|
| Stage dotnet, wall | 10,374 s | **4,871 s (-53 %)** |
| Stage dotnet, processor time (counted by the ratchet) | 16,534 s | **23,302 s (+6,768 s, +41 %)** |
| Machine's busy time over the stage | about 52,000 s | 59,028 s |

The groups (seconds from the slot taken; all in parallel):

| Group | Ended after | Tests |
|---|---|---|
| projects (every other test project) | 350 | 823 |
| self-noninterference | 876 | 8 |
| self-company | 1,358 | 2 |
| self-http | 2,631 | 1 |
| gates-rest | 2,970 | 287 |
| listvolume | 3,142 | 2 (one failed, below) |
| http-1 | 3,624 | 1 |
| http-3 | 3,829 | 1 |
| http-0 | 3,939 | 1 |
| http-2 | 4,454 | 1 |
| noninterference | 4,858 | 1 |
| http-judge | 4,871 | 1 |

The longest group is now `G1NonInterferenceTests` (80 min with everything else running; 55 min in the
round-19 run), then the shares (60 to 74 min; 34 to 44 min each when run alone on a quiet machine, 56 to
65 min when the four ran together). The stage is bound by the machine, not by one test: 59,028 s of
busy time over 16 threads is 3,690 s at the very best, and the stage took 4,871 s.

**Processor time.** The work is the same, the counted time is not: the four shards run one after the
other on this machine used 11,020 s of processor time together (user + system, `time -v`) against the
10,894 s the one unsharded test used in the p09 fixer's alone run; the same four shards run together
used 13,959 s (load 15 to 20 from other agents' work); the whole stage with twelve groups at once, 23,302 s
(load 37 on average). The processor time of one piece of work rises with the number of threads competing
for the 8 cores behind the 16 threads (hyper-threads sharing a core, context switches, caches), so
running more processes at once shortens the wall time and lengthens the counted processor time. **`verify.cpuSeconds`
would measure 23,302 s for the dotnet stage on this tree where it measured 16,534 s: the whole verify
would be about 26,400 s against the maximum of 20,800 s. The ratchet is untouched; these numbers are for the owner before any change.**
Ways to bring the counted time down are a cap on how many groups run at once (a knob not built: no run of
the in-between settings was measured), fewer, larger shares, or the owner raising the maximum.

**One test failed in the measured run**: `G1ListVolumeTests.Every_list_answers_each_tenant_from_its_own_rows_at_demo_volume`,
one answer: tenant B `GET /api/audit/log?take=100&search=%D8%A7&skip=119380` answered 500 after
0.9 s (no trace id in the report: the test did not annotate it; it does now). That group is unchanged
by this round except that it runs in a process of its own. Run again alone on the final commit
(load 10 to 55 from other agents, 32 min 40 s) it passed, so the 500 is one intermittent answer under
twelve groups at once, cause unknown; a 500 of this kind now carries the server's log entry. The integrator
should treat a repeat as a real failure (CLAUDE.md rule 9), not retry it.



## Steadier processor time (goal 2): what was found

Not reached; stated plainly. With one alone measurement of each way there is no spread to show for the
new way. What the runs of this round show about where the variance comes from:

- **Which work is counted.** Four shards run one after the other cost 11,020 s of processor time in
  total, twelve groups at once 23,302 s: the same tests cost 2.1 times as much when the machine is
  oversubscribed. Variation in how much of the machine other work takes (the critics' verifies, load 5 to 90
  during this round) moves the counted time by thousands of seconds, which is the 3,000 s of run-to-run
  noise seen between alone runs (16,077 to 19,067 s): the alone runs share the machine with their own
  PostgreSQL containers (64 % of its busy time) and any stage that overlaps.
- **Tenant B's concurrent reader** reads every GET over and over for as long as tenant A's phases 2 to 3 run, so
  its request count follows the wall time of those phases: 50,900 to 161,908 requests per share (339,664 in all) at
  about 3 ms each, 150 to 500 processor seconds per share. It is the one part of the attack whose size is
  decided by speed and not by the request list. Not changed: a fixed count would be a lower rate in the slow
  list-answer phase (2c, 1,343 s of one share, with no counted tenant A request), which the gate's
  interleaving needs.
- **Random draws** (`ControlFor`, scrambles): a few thousand draws that cost microseconds; not a source.
  The one `Random` in the attack (answer shapes) already has a fixed seed. The shares are dealt out
  by a fixed rule from the inventory, so the same code gives the same shares in every process.
- **JIT and tiered compilation**: already tuned in rounds 8 and 9 (TieredPGO off); each of the twelve
  processes JIT-compiles the test assembly again (about one minute of processor time each, not measured
  separately).

Candidates left for the owner's decision with the numbers above: a cap on the groups at once (fewer
processes, less oversubscription, longer wall time), or measuring the counted time on a machine with the
hyper-threads off.

## Notes for the integrator

- `build/verify-inside.sh dotnet` has changed (groups, the partition check, the judge). `./erp` is unchanged;
  the stage's log is `dotnet.log` and per group `dotnet-<group>.log`; the old `dotnet-suite.log` is gone.
  A kept failed folder has `trx/<group>_*.trx`, `tests-all.txt`, `tests-groups.txt` and
  `g1-http-shards/*.json` (each share's numbers).
- The stage needs about 22 GB of memory at its peak and 14 PostgreSQL containers at once.
- A critic's plant run with `--filter FullyQualifiedName~G1HttpIsolationTests` still runs the whole
  attack in its process (share after share); a plant run that sets `ERP_GATE_SHARD_DIR` needs the four
  shares' tests (`--filter Shard=k`) first.
- Tests run in this round, by hand, on the branch: `HttpAttackShardTests` (16), each of the four
  shares alone (passed), the four shares together and the judge over their results (passed), the whole dotnet
  stage once (alone, through the slot script: every group passed but `G1ListVolumeTests`, which passed on its own
  rerun), `ShardedAttackSelfTests`, the source gates and `Process=self-http` only inside that stage run
  (passed there; run before the last two small changes, which touch `ListVolume.cs`'s message
  and the diagnostics of a failed write).

## Decision 3 (owner decision #51): a cap on the groups at once, default 2

`ERP_VERIFY_TEST_GROUPS` (documented in `erp`, passed into the toolbox; read in `build/verify-inside.sh dotnet`)
caps how many of the twelve groups run at once; a waiting group starts when one ends, the longest first. Default 2.
Nothing else changed: every group, filter, partition check and judge is as above, and every test still runs once.

Measured: the dotnet stage alone (`build/verify-inside.sh dotnet` in the toolbox as `./erp` runs it, through
`ERP_VERIFY_ALONE=1 verify-slot.sh`, one setting at a time, machine quiet at the start, load 0.2). Every setting: all
12 groups passed, 842 tests, no 500 from the audit log.

| Groups at once | Wall | Counted processor time (cpu-dotnet) | Load mean / max |
|---|---|---|---|
| 1 (r19, one process; not this branch) | 10,374 s | 16,534 s | |
| 2 | 7,872 s (-24 %) | 18,520 s (+1,986 s) | 6.6 / 18.4 |
| 3 | 5,967 s (-42 %) | 19,598 s (+3,064 s) | 10.4 / 27.7 |
| 5 | 5,371 s (-48 %) | 20,832 s (+4,298 s) | 16.1 / 39.4 |
| 8 | 4,551 s (-56 %) | 22,119 s (+5,585 s) | 34.2 / 72.4 |
| 12 (earlier run) | 4,871 s (-53 %) | 23,302 s (+6,768 s) | 37.6 / 99 |

Counted time rises with every group added (hyper-threads, context switches, caches), and the wall gain flattens
(5 to 12 groups gain 500 s). Default 2: it is the setting nearest the old figure (18,520 s is inside the range
16,077 to 19,067 s that near-identical trees gave before this round) and still saves 2,500 s of wall time. 3 is the
next step (-18 % more wall for +1,100 s). No setting measured at or below 16,534 s. About 2,000 s at setting 2 are
the cost of twelve processes instead of one (JIT and start of each, tenant B's own work repeated in each of
the four shares, its concurrent reader). A whole verify at the default would count about 21,600 s (dotnet 18,520 plus
web, e2e and timing about 3,100 s) against the maximum of 20,800 s and the 21,300 s re-integration limit:
**the numbers go to the owner before any change to the maximum (#51); the ratchet is untouched.** The caps are for the
owner to choose, an integrator can pass `ERP_VERIFY_TEST_GROUPS=<n>`.

The 500 on `/api/audit/log?...skip=119380` (G1ListVolumeTests) did not occur in five full runs of the stage (2, 3, 5, 8
and the earlier 12-group run's rerun): no cause found, nothing changed for it; a 500 now carries the server's log entry.

Not done: cutting G1NonInterferenceTests by endpoint, and cutting gate processor time (goal 2): no safe saving was found
that keeps every request and check, and the session was spent on the four measurements (about 7 h of wall time).
