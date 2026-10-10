# p00: `./erp verify` processor time, round 9 (measured, nothing removed)

Date: 2026-10-10 (round 9). Piece: p00-foundation. Status: accepted.

## Context (routed by the lead, priority 1)

Integration and critic verifies used 9,809 to 10,402 processor seconds against the owner's
maximum of 10,500 (`verify.cpuSeconds`), the .NET stage about 8,000 of them. The next piece to add
tests would fail on processor time alone. The cut had to come from the shared machinery, with no
check removed, narrowed or sampled away, and the maximum (the owner's) unchanged.

## Measured before

`./erp verify` of `2274b06` (the integration branch as this round began), a scratch clone, through
the verify slots, 13:24-14:34 UTC. The machine was not quiet: other pieces' builders and verifies
ran throughout (one-minute load 1.2 at the start, 55 at the highest, 27 on average over 140
samples, 10.7 at the end, on 16 CPUs). There was no quiet hour on the owner's PC this round; every
figure below says the load it was taken at.

| Stage | Processor s |
|---|---|
| .NET (build, unit, integration, gate tests) | 7,646 |
| web and comparison harness | 1,272 |
| end to end | 267 |
| timing budgets | 49 |
| **total** | **9,234** (wall 4,193 s) |

## Where the .NET stage's processor time goes (measured)

An instrumented copy of the .NET stage (not committed: a process report written at exit with the
process's processor time, PBKDF2 derivations and their time, garbage collections and JIT counts)
and an EventPipe trace of the HTTP attack's self-test (4 minutes: managed samples and allocation
ticks, read with TraceEvent):

| Test process | CPU s | PBKDF2 derivations | allocated | methods JIT-compiled |
|---|---|---|---|---|
| gate suite (G1 HTTP attack and every other gate test) | 2,674 | 2,119 | 388 GB | 146,325 |
| HTTP attack self-test (planted module) | 4,777 | 3,392 | 782 GB | 84,140 |
| company attack self-test | 740 | 823 | 125 GB | 73,525 |
| non-interference self-test | 373 | 389 | 22 GB | 71,902 |
| identity tests | 75 | 271 | 1 GB | 68,774 |
| tenancy, reports, kernel, lists tests | 85 | 153 | 2 GB | 190,915 |

- **Password hashing** was the largest single use: 7,147 PBKDF2-HMAC-SHA512 derivations at the
  production cost of 210,000 rounds, about 0.11 processor seconds each on this machine when quiet
  (more under load): about 800-1,000 of the stage's seconds. Every test sign-in derives the typed
  password at the stored hash's cost, and the gates sign in hundreds of thousands of requests'
  worth of attackers (an attacker signs in again after every request to the sign-out endpoint).
- **In-memory lists' LIKE**: `ListBinding.MatchesLike` built a regular-expression pattern and went
  through the shared regex cache (fifteen entries, one lock) for every row of every in-memory list
  request: 13 % of the self-test's allocations in the traced window and the largest source of lock
  contention (`RegexCache.Add`), because hundreds of distinct patterns from concurrent requests
  kept evicting each other and every match parsed its pattern again.
- The rest is spread over the work itself: 617,732 requests in the product's HTTP attack (about
  1.2 million in its planted twin), 7.1 million SQL statements, PDF rendering and PdfPig reading
  of the printed answers, garbage collection (pauses 90 s and 178 s in the two attack processes).
  Runtime settings were measured and left alone: `DOTNET_TieredCompilation=0`, `TC_QuickJitForLoops=0`,
  `TC_CallCountThreshold=200`, `gcConcurrent=0` and a 256 MB first generation changed the identity
  and tenancy tests' processor time by less than the run-to-run noise at this load (65.9 and 83.9 s
  for the same default run).

## Decisions

1. **Test hosts hash with 10,000 rounds** (`Erp.Kernel.Security.PasswordHashing`). The cost of new
   hashes is a service: the production cost (210,000) everywhere, and a lower one (10,000 to
   210,000) only when `Erp:Auth:PasswordIterations` is set **and** the host environment is
   `Testing`; set in any other environment it refuses to start. The test library hands
   `ERP_TEST_PASSWORD_ITERATIONS` (set by the .NET test stage of `./erp verify`, value 10,000) to
   its Testing hosts only (`ErpAppFactory`), so hosts that tests build in other environments to
   check what the product refuses at start-up keep their own behaviour, and the timing budgets,
   the demo, the end-to-end stack and every deployment hash with the production cost. Stored hashes
   keep their own cost; the renewal of an outdated hash compares against the process's own cost;
   the decoy of an unknown address keeps the production cost. Every code path and every check is
   unchanged; only the number of PBKDF2 rounds of hashes the tests make differs. Tests:
   `PasswordHashingTests` (production cost without the setting in every environment, refusal
   outside Testing, bounds, no shipped configuration sets it, only the .NET stage of
   `verify-inside.sh` does).
2. **An in-memory LIKE is compiled once per query** (`Erp.Kernel.Lists.LikePattern`): the
   pattern becomes one `Regex` when the query is built, and each row is matched against it. Same
   semantics (`LikePatternTests`: wildcards, escapes, case, Arabic, line breaks, many threads at
   once); production in-memory lists get the same saving.
3. **No new check is cheaper by being smaller.** The G1 additions of this round (tenant B's own
   requests sent by tenant A, documented values together) add requests; tenant B's requests that
   are exactly tenant A's own are sent once (35 replays of requests that name B's records, instead
   of 1,054 of which 1,019 duplicated tenant A's own requests).

## Measured after

**Side by side, same load** (the fair comparison on a busy machine): the base commit `2274b06` and
this branch, each in a toolbox container of its own started together, the same test filters in
the same order, processor seconds of the test runs only (cgroup), 2026-10-10 17:52-18:00 UTC, load
27-32:

| Tests | Base | This branch | |
|---|---|---|---|
| identity module tests (100), first run | 87 | 63 | -28 % |
| G2 takeover, G1 company attack, G2 permissions (31 gate tests) | 332 | 247 | -26 % |
| identity module tests (100), second run | 75 | 36 | -52 % |

Every test passed on both sides with the same counts.

**Whole `./erp verify` of this branch** (`4b46271`, merged with the integration branch at
`0476d65`, which brought p04's palette work and its tests), 16:47-17:51 UTC: every test passed
(.NET 583, web unit 438, end to end 95, harness 373, verify machinery 6) and the ratchet passed,
**10,422 processor seconds** (.NET 8,159, web 1,783, end to end 410, timing 70), wall 3,844 s. The
machine was busy throughout: load 10.1 at the start, 54 at the highest, 30.8 at the end over 258
readings (the run was "not judged" for wall time, correctly: the new sampling saw the busy middle).

Processor seconds on this PC move with the load far more than the work does: the round-8 critic
measured the same commit at 8,694 s and at 11,671 s, and the base commit here used 9,234 s at an
average load of 27. With 16 hardware threads on 8 cores, a thread's processor second does less
work while the core's other thread is busy, so the same work costs more seconds on a busy machine.
Against that noise the whole-verify figures cannot show a cut of this size; the side-by-side
measurement can, and shows a quarter to a half off the sign-in-heavy tests. The web stage's growth
(1,272 to 1,783 s) and the end-to-end stage's (267 to 410 s) came with the merged p04 work (a new
palette plant self-test and end-to-end tests) and the load, not with this round's changes (the
verify machinery's own tests use under 5 s). Not done here, and left for whoever owns them: the
comparison harness's own tests use 650 processor seconds of the web stage and its mutations 240
(p01); the four web plant self-tests 550 (one vitest start per plant).

No quiet hour came this round, so there is no quiet-machine figure; the next quiet verify
(integrator or critic) gives one.
