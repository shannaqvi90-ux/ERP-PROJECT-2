# p03 — the process-state walk: the app's roots only, and a walk that stops says so

Date: 2026-10-08. Piece: p03-identity (round 5). Status: accepted.

## Context

A full `./erp verify` of this branch failed one gate self-test:
`The_process_state_check_catches_a_static_cache_and_a_stateful_singleton` did not find the planted
static memo of a generic type (`LeakyModule.ScrollTotals<Person>.Remembered`, critic p05 round 4,
plant L6). Run alone, or after the same self-tests in the same order, it passed.

The reachable-state walk (`ReachableState.Inspect`) stops after 500,000 objects. In the self-test
environment the gate assembly is loaded as a module, so `ProcessState.Inspect` took every static
field of the gate's own instruments as a root, among them the SQL trace (`SqlTrace.Calls`,
`Binds`, `Settings`, `Changes`), which holds every traced call of every test in the process. Other
test collections run in parallel in the same process (the HTTP attacks among them), so by the time
the self-test ran the trace could fill the budget before the walk reached the app's last roots:
the statics of generic instantiations, where the plant sits. The walk then stopped without saying
so. Measured in a small run: 24,802 objects walked after two self-tests, about 1,150 of them the
app's.

`ProcessState.LiveRoots` (the fingerprint the HTTP attack takes before and after) already left the
instruments out, for the reason written there: the gate's instruments change while they measure,
and only the planted code in the gate assembly is the app's. In the product's own environment the
gate assembly is not a product assembly, so the instruments were never roots there.

## Decision

1. `ProcessState.Inspect` walks the same roots as `LiveRoots`: the app's singletons and statics,
   without the gate's instruments (namespaces `Erp.Gates.Tests.*` other than
   `Erp.Gates.Tests.SelfTests`). The static-field inventory (`InspectTypes`) is unchanged and still
   covers every type, the instruments included. In the product's environment nothing changes.
2. The walk reports when it stopped at its object budget (`Result.Cut`,
   `ProcessStateInventory.ReachableWalkCut`). The G1 process-state gate fails when its walk was
   cut, and so does the self-test above: a walk that stops early is blind to what it did not reach.
3. A new self-test plants 700,000 objects ahead of a planted catalogue and requires the walk to
   report that it stopped.

## Rejected

- Raising the budget: the trace grows with every test, so any number is luck.
- Walking the instruments last: they would still be walked (processor time for nothing the app
  holds) and, filling the budget, would make the new "walk was cut" check fail every full run.
