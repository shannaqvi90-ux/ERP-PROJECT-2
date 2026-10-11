# p04 — Test processes do not spin idle thread pool workers

Date: 2026-10-10. Piece: p04-shell, round 9. Status: accepted.

## Context

Round 8 could not be integrated. Two full verifies of the merged tree had every test green
(.NET 551, web unit 426, end-to-end 93, comparison 373) but counted 11,005 s and 10,681 s of
processor time against the owner's maximum `verify.cpuSeconds` 10,500 (needs-human #12). The .NET
stage was 8,775 s and 8,610 s of it. The machine was busy with other agents' verifies during both
runs. The maximum may not be raised and no test, request, attack or minimum may be narrowed
(CLAUDE.md rule 9), so the work has to cost less.

## What was measured (this machine, 2026-10-10)

The G1 HTTP attack (`G1HttpIsolationTests`) was run alone and watched with `dotnet-counters` and
per-thread processor time from `/proc/<pid>/task/*/stat`:

- about 1.75 processor cores in use while it ran, 7,000 thread pool work items a second, gen0
  collections 10 a second, gen1 2.5 a second, no gen2 collection in the window, GC pauses about 4%
  of wall time;
- almost all processor time on thread pool workers; the finalizer, background GC and timer threads
  together under 1%;
- a third of the process's processor time was system time.

The test processes host the app in memory and spend most of each request waiting on PostgreSQL.
A .NET thread pool worker that runs out of work spins (by default up to 70 iterations of its
semaphore's spin loop, with yields) before it blocks; with thousands of short work items a second
separated by database waits, workers keep running out of work, spin, find nothing and block, and
the next completion wakes one again.

A/B pairs, each pair run side by side on the same machine at the same time (so both arms had the
same load), the same G1 non-interference test (`G1NonInterferenceTests`, 3,300 comparisons and
every other count unchanged):

| Pair | Default (spin limit 70) | Spin limit 0 | Wall time |
|---|---|---|---|
| 1, `dotnet test`, limit set by environment variable | 182 s user + 85 s system = 267 s | 148 + 37 = 185 s (−31%) | 309 s and 311 s |
| 2, test executable, limit set in the runtime configuration | 218 + 98 = 316 s | 180 + 43 = 223 s (−29%) | 382 s and 383 s |

Both arms passed. Wall time did not change: the spin saved no latency here, it only burned
processor time.

## Decision

Every test project sets the runtime option `System.Threading.ThreadPool.UnfairSemaphoreSpinLimit`
to 0 (`tests/Directory.Build.props`, `RuntimeHostConfigurationOption`, written into each test
project's `*.runtimeconfig.json`). It applies wherever the tests run: `./erp verify` inside its
toolbox container, `dotnet test` on a developer's machine, and the test executables run directly.

This is runtime tuning of the test hosts only. No test, request, value, attack, plant, timeout,
re-run or minimum changed, and no maximum moved. The product's own runtime settings (the
deployable `Erp.Web`) are untouched: whether production should spin is a separate decision, to be
measured under production load, not under a test harness's.

## Rejected

- *Narrowing the planted-module self-test of the HTTP attack* (it runs the whole attack again, about
  55 minutes in a verify): its assertions also check that the product's own endpoints show no leak
  in that environment, so cutting it narrows a gate (rule 9).
- *Raising `verify.cpuSeconds`*: the owner's maximum (rule 9 and needs-human #12).
- *Other runtime settings* (TieredPGO off, non-concurrent GC, a larger gen0 budget): measured in
  round 6 (docs/decisions/p04-shell-processor-time.md) and cost more; the counters above show GC
  and the finalizer are not where the time goes.

## Processor time this round adds

Negative: no gate work was added this round. The test processes' processor time falls by about
30% on the measured test; the verify figures are in the round's notes for the integrator.
