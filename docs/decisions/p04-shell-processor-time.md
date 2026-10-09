# p04 — The Arabic sessions and the verify's processor-time maximum

Date: 2026-10-08. Piece: p04-shell, round 6. Status: accepted.

## Context

Round 5 could not be integrated. Every test passed on the merged tree, but the ratchet's
processor-time check (`verify.cpuSeconds`, maximum 9,000) read 9,412 s: .NET 8,317, web 847,
end-to-end 210, timing 37. The verify ran with a load average of 47.6 at its start. The integration
branch alone had used 7,615 s (.NET 6,572) in an earlier verify. The maximum may not be raised, and no
test, attack or minimum may be narrowed (CLAUDE.md rule 9).

## What was measured (this machine, 2026-10-08)

Each pair below ran at the same time on the same machine, so both runs had the same load. A copy of
the integration branch (810b289, with only verdict records added since) was built in a scratch folder
next to this branch. Processor time is the test process's user plus system time from
`/proc/<pid>/stat`, the same work the verify's cgroup counts.

| Test (alone, `dotnet test --filter`) | Integration branch | This branch | Difference |
|---|---|---|---|
| `G1HttpIsolationTests` | 1,596 s | 1,962 s | +366 s (+23%) |
| `G1NonInterferenceTests` | 191 s | 251 s (with a sampling trace attached) | +60 s |

The HTTP attack's own report, with the Arabic counts per phase added this round, shows where the
Arabic requests go: phase 1 (every route) 17,135 of 199,235 requests; phase 2 (route and query
parameters) 65,217 of 259,659; write pairs 4,366; tenant B's Arabic administrator 33,867 of
136,029. The Arabic requests cost what the English ones cost: about 3 ms of processor time each,
request and judgement together.

The same tree costs more processor time when the machine is busier:

- `G1HttpIsolationTests` on this branch: 1,962 s with a load average near 30, 2,137 s near 60.
- The web stage's steps run one after another on this host at a load near 20: 520 s in all
  (type-check 9, string check 2, unit tests 42, the three plant self-tests 62 + 77 + 140, build 11,
  comparison harness tests 146, `npm ci` from cache a few). The verify at load 47.6 counted 847 s
  for the same steps.

Busy hardware threads share their cores and caches, so the same work takes more processor
seconds. The round-5 merge's 9,412 s therefore combines this piece's work (about +850 s: the HTTP
attack and its self-test on the planted module about +370 s each, non-interference and its self-test
about +60 s each) with the load of that run.

## Decision

Make the work cheaper. Every request, value, attack, plant and minimum stays as it was.

1. **Lists held in memory are interpreted, not compiled** (`src/Kernel/Erp.Kernel/Lists/InterpretedQuery.cs`).
   LINQ's in-memory provider (`EnumerableQuery`, used by `AsQueryable()`) compiles every query it
   runs to IL. A list request ran two or three of them (count, page, groups), and the roles list is
   held in memory. A runtime trace of the HTTP attack showed about 360 methods compiled per minute
   for that list alone while phase 1 ran. `ListBinding` now hands an in-memory source to a provider
   that rewrites the `Queryable` calls to the `Enumerable` calls of the same name and shape and runs
   them with the expression interpreter. The rows, their order and their values are the same
   (`InterpretedQueryTests` compares them with the compiled provider's over filters, sorts, paging,
   groups with sums, keyset conditions and Arabic text). Running the same query again compiles no
   method. The existing list engine tests (in-memory lists) all run through it. Production gains
   the same saving on every roles-list request.
2. **A planted gate run stops at its first failed test** (`web/scripts/plant-self-test.mjs`,
   `identity-plant-self-test.mjs`, `tenancy-plant-self-test.mjs`: `vitest --bail=1` for planted
   copies only). One failed assertion is what catches a plant. The tests after it only cost
   processor time. Each plant must still fail the gate on an assertion, and the control (no plant)
   still runs every test and must pass. Measured on this host: 62 → 44 s, 77 → 42 s and 140 → 82 s,
   with all 41 plants still caught.
3. **The HTTP attack reports Arabic requests per phase**, so the next change of this kind can be
   judged from the test's own output.

## Rejected

- *Fewer Arabic values or names* (for example, leaving the undocumented query names to the English
  sessions in phase 2, about three quarters of that phase's Arabic requests). This narrows an
  attack (rule 9) and was not done.
- *Runtime settings for the test processes* (`DOTNET_TieredPGO=0`, `DOTNET_gcConcurrent=0`, a
  256 MB first GC generation). Measured side by side on the HTTP attack: 2,356 s against 2,137 s
  without them, so they cost more.
- *Raising `verify.cpuSeconds`*: weakens the bar (rule 9).
- *A cache of answers shared between requests*: process-wide state holding tenants' data, which the
  G1 gates rightly refuse.

## For the integrator and the lead

The maximum is a processor-time count, and on this machine that count rises with the load of
everything else running. The same tree measured hundreds of seconds apart at different loads. A
verify started under a load average near 50 can exceed 9,000 s even when the work did not change.
Whether to judge the maximum only on a quiet machine, or to normalise it by load, is the owner's
call. It is listed here, not decided here.

## Merge with the integration branch (round 6, 2026-10-09)

The integration branch reached the same two savings independently: `InMemoryQuery` (kernel,
`src/Kernel/Erp.Kernel/Lists/InMemoryQuery.cs`, with `InMemoryQueryTests`) and `--bail=1` for
planted copies in the three plant self-tests. The merge keeps those as the only copies and drops this
piece's `InterpretedQuery.cs`. This piece's tests (the same rows and order as the compiled provider over
nine query shapes, no method compiled when a query runs again, operators staying in the provider) now
run against `InMemoryQuery` as `InMemoryQueryAgainstCompiledTests`. This piece also routes the list's
`Apply` (exports, bulk actions on everything that matches) and `Matching` through it, not only the
list page, so an export of an in-memory list is interpreted too.

The owner raised `verify.cpuSeconds` to 10,500 on 2026-10-08 (needs-human #12). This piece did not
change it.

## Processor time of this round's merged tree (2026-10-09)

This round adds no gate work of its own: the merge, one fix to the write oracle's sign-ins (it names
each administrator's workspace) and the decision record. Measured through the verify slot on the
merged tree (f380b2e, load average about 8 at the start, up to about 40 while it ran): .NET stage
6,240 s, web 1,039 s, end-to-end 252 s, about 7,530 s in all before the timing stage, against the
maximum of 10,500 s. The .NET stage failed only on the write oracle's id pass (B's administrator's
e-mail also existed in tenant A, so the sign-in asked for a workspace); the processor time is that of
a full .NET run, since every other test ran. The G1 HTTP attack took 25 min 36 s of wall time in it.

The passing verify of this round (6f6e379 with this record's first paragraph, slot taken 2026-10-09
at about 05:25 UTC, load average 32 at the start, 49 to 59 during the .NET stage, 21 at the end)
counted 10,484 s: .NET 8,478, web 1,612, end-to-end 332, timing 62, maximum 10,500. Every stage of
the same tree cost 35 to 55 per cent more than in the run above at a lower load (.NET 6,240 against
8,478; web 1,039 against 1,612). A verify of this tree under a load average near 50 or more can
therefore exceed the maximum with no change to the work; on a quieter machine it uses about 7,600 s.
