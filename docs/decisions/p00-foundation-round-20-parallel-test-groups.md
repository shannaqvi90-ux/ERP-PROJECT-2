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
`self-noninterference`), `self-shards` (new, below), `listvolume` (G1ListVolumeTests), `gates-rest` (every
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
- `ShardedAttackSelfTests` (new process `self-shards`): the planted module's leaks, filled through an
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

(Measurements, processor time and notes for the integrator follow in the sections below.)
