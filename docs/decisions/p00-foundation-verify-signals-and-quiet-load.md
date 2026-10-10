# p00: `./erp verify` ends every process it started, and judges "quiet" over the whole run

Date: 2026-10-10 (round 9). Piece: p00-foundation. Status: accepted.

## Context (routed by the lead)

1. **Survivors after a signal.** A SIGTERM to `./erp verify` ended the script but left its stage
   processes running: the end-to-end stage's loop waiting for the .NET stage, `docker run`, and the
   `tee` and `sed` of every stage's output pipeline (p04 round 7). They inherited the verify slot's
   file descriptor (`gauntlet/tools/verify-slot.sh` holds the slot with an open descriptor), so the
   slot stayed taken for about 40 minutes. `verify_cleanup` sent `kill` to each stage's subshell
   only; the subshell's own children (the pipeline) were orphaned, not ended. And the script had no
   trap for TERM: bash ran the EXIT trap only on some paths.
2. **Quiet judged from two readings.** `verify.quietSeconds` was judged when the one-minute load
   was low at the start and at the end. Runs that were busy only in the middle (p02 r8, p03 r8,
   p01 r10: load 0.28 at the start, 68-74 mid-run, 4.76 at the end; 4,283 s) were judged quiet and
   failed with every test green.

## Decisions

1. **Stages end with everything they started** (`build/verify-processes.sh`, sourced by `./erp`).
   `stop_process_trees` takes the process ids of this run's stages (only ids whose parent is this
   `./erp` itself, so a stage that ended and left its id to an unrelated process is never touched),
   collects each one's whole process tree from `ps` (by parent id, never by name), freezes the
   trees with SIGSTOP so nothing starts another process meanwhile, collects again, sends SIGTERM and
   SIGCONT, and after a grace (`ERP_STOP_GRACE_SECONDS`, default 10) sends SIGKILL to whatever is
   left. The containers and the stack are removed by name as before.
2. **Signals go through the same clean-up.** `./erp verify` traps TERM, INT and HUP (exit 143,
   130, 129), so the EXIT trap's clean-up runs on every signal; a second signal during clean-up is
   ignored (SIGKILL still works). Bash runs a trapped signal's handler only once the foreground
   command returns, so the two steps that used to run in the foreground for minutes (the timing
   budgets, the ratchet) now run as awaited stages like the others; the wait loops poll every
   second.
3. **The load is sampled throughout.** A sampler started with the stages appends
   `<unix time> <one-minute load> <CPUs>` to `verify-load-samples` every 15 s
   (`ERP_LOAD_SAMPLE_SECONDS`) until the stages are over; it is one of the stage processes the
   clean-up ends. `build/ratchet-check.mjs` judges the wall time only when every reading was quiet
   (at most 0.5 per CPU): the start, every sample and the end. It prints the highest reading and
   how many there were. A run quiet throughout and slower than the maximum still fails; one busy
   reading anywhere makes the run "not judged". A sample file with no readable sample does not make
   a run unjudged: the start and end readings decide, as before. The maximum (3,600 s) is the
   owner's and unchanged.

## Tests

`build/verify-processes.test.mjs` (run by the web stage with `node --test`, counted by the ratchet
as `suite.buildTests`, minimum 6):

- the real `./erp verify`, with a stand-in `docker` on `PATH` whose image build and `docker run`
  sleep, in a process group of its own: once all three stages are live pipelines (two `docker
  run`, the stack's image build, `tee`, `sed`), SIGTERM; the run exits 143 and **no process of
  its group is left**, and it removed its containers and stack. Against the previous `./erp`
  (commit 8a0c732) the same test fails: the script died of the signal and its three `tee` and
  three `docker run` stand-ins were still running (they held the test's pipes open until they
  were stopped by id);
- `stop_process_trees` ends a pipeline subshell and a loop with all their children and leaves a
  sibling process alone;
- the ratchet's judgement: a run busy only in the middle (the lead's numbers) is not judged; a run
  quiet throughout and too slow fails; one within the maximum passes; one busy sample makes a run
  busy; without samples the start and end readings decide; an empty sample file does not exempt a
  slow run.
