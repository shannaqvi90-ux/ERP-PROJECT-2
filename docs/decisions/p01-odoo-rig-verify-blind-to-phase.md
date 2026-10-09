# p01: set-up cannot do the task off the clock (instrument 7)

Round 9 of p01 (Odoo reference rig and blind comparison harness). Answers the round 8 critic's
biggest gap and its other findings.

## The gap

Set-up may act on the product (it prepares fixtures). The one thing that kept set-up from doing
the task itself, off the clock, was a single `verify()` call before the clock: a run counted as
"already done" only when that call answered `verified: true`. But the driver process called
`verify(ctx, undefined)` before the clock and `verify(ctx, outcome)` after it, so a driver knew
which call it was in. It could answer "not done" (or throw) before the clock and read the real end
state after it. And nothing required the measured part to take a counted step. The critic's plant
saved the value through the API in set-up and did nothing in `run()`: 0 steps, 0 keys, 0.006 s, a
recorded win over Odoo on edit-and-save (a task that is a loss unplanted). The whole harness
suite stayed green.

## Decision

`verify()` cannot tell the calls apart, a measured part must do something, and for a task whose
end state is saved, the harness itself checks that the state arrived during the measured part.

1. **Same arguments every time.** `verify(ctx)` takes `ctx` alone, before and after the clock.
   `run()`'s outcome never reaches it; the drivers that used it (the expected third contact, the
   change log text, the printed request, "keyboard only") now read the screen, the back end or
   the harness's own record instead. The driver lint refuses a second parameter.
2. **A fresh process for each call.** Each `verify()` call (one before the clock, two after) runs
   in a process of its own (`DriverHost.forVerify`), never in the run's process, so nothing
   `run()` keeps in memory (a module variable, a global, `ctx.state`) is there. Set-up's
   `ctx.state` arrives as data, snapshotted when the start screen is ready. The files the measured
   part downloaded arrive as `ctx.downloads`, copied by the harness into a folder only it writes,
   with what the page sent between the click and the file (Odoo's print request); the list is
   empty before the clock, and only a counted step makes a download.
3. **Nothing else to tell the calls apart by.** A verify() process reads only the harness's code
   and data, the driver's folder and the downloads (not the run's scratch folder, the shots and
   results, `/proc`), writes nothing (a file's time is a clock), and has no clock: `Date`,
   `Intl` formatting without a date, `performance.now`/`timeOrigin` (prototype included),
   `process.hrtime`/`uptime`, `os.uptime` and `os.cpus` times stand at the moment the run began;
   the product's `Date` header is stripped from its answers. Timers still run (the process needs
   them); a pause of over a second in `verify()` already makes a run invalid.
4. **No step, no task.** A measured part with no counted step is refused (invalid).
5. **The saved state arrives during the measured part.** A task whose end state is saved in the
   product declares `saves: true` (13 tasks; every task whose done text names the back end must).
   The harness records every back-end read of each `verify()` call (method, address, body, a
   JSON-RPC id ignored) with its answer, and requires a part of one answer that differs after the
   clock from before it and is the same in both passes after it. The second pass starts at least
   1.1 s after the first, so a part that tells the time (to the second) never counts. A JSON answer
   is compared part by part: Odoo's session information carries a value that changes with every
   answer, and the user it names is what was saved.
6. **What the person enters arrives then too.** A task that names what the person types
   (`enters`, 7 tasks) requires the changed part to hold an entered value that the read did not
   hold before the clock, so a trivial counted step that changes something else (a version
   number) does not pass. And the start screen's fields may not already hold an entered value.
7. **Keyboard-only is the harness's rule.** `keyboardOnly: true` tasks fail on a pointer step; the
   drivers no longer report "keyboard only" through the outcome.

The critic's P1, P1b and P2 are self-tests now (`test/before-clock.test.mjs`), with P3 (a mark in
the run's memory), P4 (a file), P5 (every clock), P6 and P6b (the screen as the signal), P7 (a
version number changed instead), P8 and P8b (the value already in the form, or only beside it) and
P9 (a back-end clock). Mutations M24 to M33 remove each defence in turn.

## Other findings closed

- **Critic mutations A1, A16, A26** (a chord counted as one key, the reference on its worst path,
  a scroll with no step) and the defence-in-depth gaps **A24** and **H10** (no refusal of requests
  after the clock, no 0.5 s limit on verify reads) each have a self-test and a mutation now (M34 to
  M38). Writing the A24 test found a real window: a request a page starts without script (an SVG
  animation, a style) between the clock stopping and the refusal being installed reached the
  product. The refusal is installed first now, before the freeze.
- **Blindness:** demo names inside form fields (our company form's "Al Noor Trading LLC" and
  ALN-DXB) are painted over by the same rules as text (`revealsIdentity`); every field is painted
  when the values cannot be read. The review page shows no columns for a task one product cannot
  run yet ("not built" named its product). Mutations M39 and M40.
- **Odoo's shortest find-user path** (routed from p05 r7): variants `shortest-suggestion`,
  `shortest-enter` and `palette-shortest` type the shortest piece of the name that puts the user on
  the users list's first screen (found through the back end in set-up, as ours finds its shortest
  prefixes): "il pi" and the suggestion, 5 keystrokes, as the critic found.
- **The shared rig falling short** (needs-human #13): the 100,000 check is unchanged. `up.sh` now
  keeps 100,000 job runs from the last day (spread over twelve hours instead of six days), so a
  top-up lasts six days instead of about one; `run.mjs` warns two days before a vacuumed list falls
  short and names both commands; `tools/odoo-reference/README.md` has a section on it.
- **The reference sign-in's returning variant** signs a browser that lands signed in out first.

## What it cannot do

The instrument cannot tell whether a driver's `verify()` reads the right thing; it makes sure
`verify()` reads the same way before and after the clock and, for a task that saves, that the state
it reads changed during the measured part. For a task that saves but names nothing entered (a
language switch, an approval), a counted step that happens to change another stable part of an
answer `verify()` reads would still pass; drivers are reviewed code and the lint and plants cover
the known shapes.

## Alternatives not taken

- Replaying run()'s outcome to the before-clock call: the outcome does not exist before the clock.
- Freezing the verify() process's timers too: Node needs them, and the pause meter already refuses
  a verify() that waits.
- Recording set-up's writes and refusing a run whose set-up wrote what verify() reads: set-up
  legitimately writes the start state (removing an earlier copy, a language reset), and telling a
  restore from doing the task needs the task's meaning; the saved-state rule judges the effect.
- Routing every request through the harness during the measured part (to refuse after-clock
  requests with no window at all): it would add interception latency to every request of both
  products, more for the one that makes more requests.

## Cost

Each run starts three more small Node processes (one per verify() call, about 0.1 s of processor
time each) and, for a task that saves, waits 1.1 s between the two passes after the clock (wall
time, not processor time). This round adds 37 harness tests (352 in all) and 21 instrument
mutations (41 in all), each mutation running only the self-tests named for it.

Measured in `./erp verify` at this branch's head on 2026-10-09 (slot taken 18:47:23Z, load 25 at
the start and 26 at the end on 16 CPUs): passed in 3,644 s; processor time 9,570 s against the
owner's maximum of 10,500 s (dotnet 7,804, web 1,465, e2e 242, timing 59). The web stage, which
holds the harness tests and the mutations, used 1,465 s against round 8's 1,340 s (critic's verify
of 59e3533 at load 26), so this round adds about 125 s of processor time. Most of the rest of the
difference to round 8's 8,414 s total is the .NET stage (7,804 s against 6,771 s), which this piece
does not touch (other pieces' tests merged since, and the machine's load).
