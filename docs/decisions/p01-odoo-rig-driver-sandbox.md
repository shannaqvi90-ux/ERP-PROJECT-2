# p01 — Drivers run in a sandboxed process; verification cannot wait off the clock

Round 5 critic finding (verdict `gauntlet/verdicts/p01-odoo-rig--r005--09d6d2c7dd.json`): a driver
could still act uncounted or off the clock from the harness's own Node process. A fetch captured
when the driver module loaded was the unguarded one; `process.getBuiltinModule('node:child_process')`
ran `curl` against the back end while the task was measured; `verify()` could wait for the end
state after the clock stopped; and the paste check matched chord spellings, not keys.

## Decision 1: drivers never run in the harness process

Every harness process starts one driver process (`lib/sandbox/host.mjs`) and runs every driver
hook there. The process is started with Node's permission model and a preloaded lockdown:

- `--permission --allow-fs-read=* --allow-fs-write=<scratch>`: Node itself refuses child processes,
  worker threads, native addons, WASI, the inspector and `process.binding`, and file writes outside
  the process's own scratch folder (also its `TMPDIR`).
- `--import lib/sandbox/lockdown.mjs`: the permission model in Node 22 and 24 does not cover the
  network, so the lockdown replaces, before any other module runs, every entry point Node offers to
  a socket (`net.Socket#connect`, `net.Server#listen`, `tls.connect`, `http(s).request`,
  `http2.connect`, dgram sockets, `WebSocket`, `EventSource`, `fetch`) with a refusal that cannot be
  undone (non-writable, non-configurable; `syncBuiltinESMExports` carries it into named imports).
  It also refuses loader hooks (they run in a thread of their own) and V8 flags, and refuses child
  processes, workers and the inspector a second time so that each attempt is reported.
- The driver process holds no Playwright object, no clock and no network. `ctx.page`, `op`,
  `fetch` are stand-ins that send each call over the IPC channel; the harness
  (`lib/sandbox/bridge.mjs`) performs it on the real objects through the existing guards, the
  operator and a fetch policy, judged by the phase when the request arrives. Locators are built
  in the driver process as recorded chains and replayed through the guarded objects, so each step
  of a chain passes the guard.

Why a process and not a patch: each round so far closed one way out of the shared process and the
next critic found another (round 4: page script and pending actions; round 5: a captured fetch, a
built-in module reached at run time). Patching a process that also runs hostile code cannot be
complete: the driver can capture, patch or schedule anything before or after the patch. A separate
process turns the question into "what can the driver send over the channel", and the harness
answers each message itself. The cost is one IPC round trip per call (well under a millisecond on
this machine), the same for both products.

Alternatives rejected:

- More in-process patches (wrap `process.getBuiltinModule`, ban captures in the lint): the lint is
  static and the patches race with the driver's own code; the critic asked for a structural fix if
  possible.
- `vm` contexts or worker threads: same process, same capabilities, and Node documents `vm` as no
  security boundary.
- Operating-system isolation (network namespaces, containers): not portable to every machine that
  runs `./erp verify`, and Unix-socket paths cross network namespaces anyway; the permission model
  plus the lockdown covers both on every Node 22.13+ and in the toolbox image (Node 24).

What a driver may still do: read files (the dataset, downloads), write under `os.tmpdir()`, and
send requests the harness allows by phase. Set-up and verification reach the product through the
harness's fetch, only on the product's own origin. Files the harness writes for a driver (a
screenshot `path`, a download folder) must lie in the scratch folder. API transports (how a typed
request is carried) are the harness's own, by name (`lib/api-transport.mjs`), because a driver's
transport function could send more than it typed.

The harness also reads driver metadata and task definitions through the driver process, so no
module from `drivers/` or `tasks/` runs in the harness process at all. `execute()` refuses a driver
object handed to it directly.

## Decision 2: the product's answer is on the clock; verification reads once

- When `run()` returns, requests of the measured page that are still in flight keep the clock
  running until the last one ends (a system wait). Honest drivers wait for their end state, so for
  them nothing changes; a driver that returns early pays for the answer it did not wait for.
  Long-lived channels (web sockets, event streams, long polling, Odoo's bus) are not waited for.
- After the clock stops, the page's requests are aborted (`requests_after_clock`), so nothing the
  page does later can finish the task off the clock.
- In the verifying phase every wait is refused and every read times out after 0.5 s.
- `verify()` runs twice, timed. A first pass more than 0.5 s and more than twice as slow as the
  second waited for the end state (by a busy loop, polling the back end, anything) and the run is
  invalid. This catches waiting without having to list every way to wait.

## Decision 3: chords are read as keys; a copy needs a selection

`parseChord` resolves `ControlOrMeta`, `Ctrl`, `KeyV`, case and modifier order. A paste needs an
earlier copy, inside the measured part, that found a selection (the operator reads it as the copy
key is pressed). The runner empties the clipboard before the start as well.

## Decision 4: KLM — no continuation across screens

The critic noted that typing right after an Enter that loaded a new screen was modelled without
an M. Each step now records the address path it began on; a step never continues one that began
on another screen. Card, Moran and Newell's rule 0 places an M before each new unit of action and
rule 1 removes it only for operators fully anticipated in the one before, which a screen not yet
shown cannot be. The rule is the same for both products.

## Consequences

- Instrument version 5; every Odoo baseline was re-captured (median of three) on the shared rig.
- Node 22.13 or later is required (`engines` in `gauntlet/compare/package.json`).
- Tests write their drivers as modules (`test/helpers/driver-module.mjs`); critics plant faults the
  same way (README, "Planting a fault").
