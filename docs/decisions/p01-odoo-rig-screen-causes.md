# p01: every browser task is judged by causes; a found record must be searched for

Round 14, answering critic p01 r13 (BLOCKED: every off-the-clock check applied only to tasks that
save; plants N1, N1b, N2 and N3 on the eleven tasks that do not save were recorded as verified wins).
Instrument version 12.

## Problem

Rounds 11-13 tie a task's end state to what the person did through the writes the page sends: the
browser's record of the page (Chromium's trace), the 1.1-2.1 s gap after verify()'s read, the declared
writes, and "entered by the measured part". All of it ran only when a task declares `saves`. Eleven
tasks do not save (find-record, which the owner's bar names, find-user, switch-company,
export-filtered-list, who-changed-field, the four keyboard tasks, the two Arabic reports). For them the
instrument checked the start screen's fields, address and home landing, that verify() fails before the
clock, and that at least one step was taken. Nothing tied the end state on screen to the steps, so set-up
could leave the task done, or half done, for the product to finish:

- N1, N1b: a setting makes the start screen open the record on its own timer; one key, or one click on
  plain text, was credited.
- N2: the server remembers the record set-up opened and offers it on the start screen as a link.
- N3 (on the real product, the real find-user driver): set-up saved a personal default view of the users
  list whose search is the needle's name; the list opened already filtered and the person pressed Enter.

The critic also found that round 13's DevTools-command rule refused an honest save under load: a
click's handler ran inside a DevTools command task.

## Decision

1. **Every browser task is traced, and every change of the screen is judged** (`lib/causation.mjs
   screenCauses`, `lib/runner.mjs screenProblem`).
   - The record starts before verify()'s read on the start screen (not at the clock) and runs until the
     page's script is frozen after the clock; the clock starts 1.1-2.1 s after verify()'s read for every
     browser task (it was only for tasks that save). A start page that finishes the task on its own once
     verify() has read it (N1: its timer fired inside the gap) is seen doing so.
   - A *change of the screen* is what Chromium records inside the task that made it, with
     `disabled-by-default-devtools.timeline.invalidationTracking`: a node inserted or removed, a text
     changed, an attribute or style changed (`StyleRecalcInvalidationTracking`,
     `LayoutInvalidationTracking`, `StyleInvalidatorInvalidationTracking` ...), a document parsed, a
     navigation started. Changes the browser makes while rendering a frame (style recalculation, layout,
     paint) follow from a change made earlier and are not judged again. Measured: without the tracking
     category the record holds one `ScheduleStyleRecalculation` per frame, so a second change in the same
     frame (a timer firing just after a step's change) left no mark; with it every change is recorded.
   - Each change is judged by the same chains as a write (input, the timers, frames, answers and
     messages it led to; another thread or a server's push causes nothing), **by where the chain begins,
     not by its delays**: a page that shows the record 3 s after a click, on a timer or behind a slow
     answer, shows it because of the click. A product configured to open a record on any click is the
     product's answer to that click (as with writes, the blind screenshots judge it), and a search that
     takes 2.5 s on a loaded machine is still the person's. A repeating timer a step armed is the step's.
   - A document is judged by the navigation that loaded it (the last one its frame's thread started),
     in whatever task its parser ran; markup that script parses (`innerHTML`, inside the page's script or
     a microtask checkpoint) is a change of that script's task.
   - What the harness itself does is passed over, and only that: a DevTools command that does not act for
     a step changes nothing of its own except while the harness prepares a blind screenshot (its neutral
     title and the screenshot's own style and masks, in script worlds of the harness's). In that window
     only page script the harness's change set off (an observer) is judged (plant N1e); at any other time
     a change inside a harness command is the page's own script run inside the harness's read (a getter
     or prototype the page replaced, plant N1f) and is judged as no step's. The blind shot's neutral title
     is now set in an isolated world, so the page's replaced setters never run there; settleDocument
     reads `readyState` in the read world. A task of the browser's own (a window's size or focus, a
     screenshot's settings: no script, no answer, no event, no navigation) is passed over.
   - Only the main frame's renderer threads are judged: a frame of another site runs in its own process,
     verify() never reads into it, and an honest page may embed one that changes on its own.
   - **Enforced for the product measured against the reference, recorded for the reference.** Measured
     on this machine (instrument 12, every task once): our product's 30 runs of the 13 built drivers
     show 0 changes no step caused (5-67 judged per run). Odoo's 37 runs show 0-73 per run: its
     client changes the screen from a shared worker, repeating timers, transitions and observers it runs
     during animations, avatars whose requests went out before the clock, and late chunks of its start
     document. Enforcing the rule there would refuse every honest baseline. The reference is a published
     image (`odoo:20.0-20260926`) no one here writes, and a set-up that helped it could only make our
     product's comparison harder; the rule refuses a run and never changes a count. `screen_changes` in
     every result records what was judged (`enforced: false` for the reference).

2. **A task that asks the person to find one record names it** (`finds`, `lib/runner.mjs findsValue`,
   `isPieceOf`, `findsProblem`): find-record and who-changed-field (`contact.name`), find-user
   (`user.name`); `test/tasks.test.mjs` keeps them declared. The run is refused when
   - the measured part typed no **piece** of the name: its letters and digits, in order, at least two of
     them (the whole name, its first letters, the initials, the start of each word, a fragment: every
     path either product's drivers take, "m an pi", "map", "il pi" included);
   - the screen showed the name (as text or a field's value) right before the first piece was typed
     (read in the harness's read world by the operator, on the clock for both products alike, a few
     milliseconds; N3: the list opened already showing the needle);
   - the start screen showed it (N2).
   A driver health check (a clean stack where the users list holds a handful of users on one screen) is
   held to the first rule only; it is not a comparison.

3. **Set-up may not put the record within reach.** In a comparison run, a set-up write (a back-end call
   through the harness's fetch, or a request set-up's browser sends) that carries the name the person
   finds, whole or any word of four letters or more (the task's own input values taken out first, so
   who-changed-field's set-up may give the contact its new e-mail), is refused (N3, N3b, N3c); and
   set-up's browser may not open an address that names the task's data (`taskWords`, N2 in the critic's
   own shape, on a task that names nothing to find).

4. **Input the browser delivers while running a DevTools command is the step's** (critic r13, other
   finding 1): a discrete input event (a click, a key, text) dispatched inside a command task, outside any
   of the page's script, chains to the step that began last before it. A click the page's own script
   dispatches there is inside its script and is not input.

## Self-tests and mutations

`test/screen-causes.test.mjs` (stand-in product, real browser): plants N1 (timer before the clock),
N1L (timer inside the clock), N1b (one click on plain text), N1c (the record shown in place, no
navigation), N1d (a poll the back end answers "open" inside the clock), N1e (an observer of the title the
blind shot neutralises), N1f (a patched `Node#contains` the harness's read calls), N2 (address naming the
record), N2b (by id; the start offers it), N2c (offered a click from the start), N2d (two letters typed,
then the remembered record clicked), N2x (the critic's N2 on a task that names nothing to find), N3 (a
default view by back-end call), N3b (by set-up's browser), N3c (part of the name); controls: the honest
search, the same behind a 0.3 s debounce and a 2.5 s answer, the initials, a task answering only the
person, and the gap for a task that does not save. `test/causation.test.mjs`: the rules on synthetic
traces (a timer before the clock, a slow answer, a repeating timer a step armed, the harness's own
change, an observer it set off, the browser's own change, a document by its navigation) and the
DevTools-command input. The critic's own r13 plant file passes unchanged (N1, N1b, N2 never verify; C1
does). Mutations M113-M128 (`npm run mutations`); M65, M66 and M80 follow the code they mutate.

## Alternatives considered

- **Judge only the changes that touch the end state's nodes** (the critic's option a, narrowly): the
  tracking events carry node ids, so the nodes showing the needle could be followed. Odoo renders its
  list rows in tasks the record cannot attribute (its shared worker is busy throughout), so the narrow
  rule would still refuse the reference; and for our product the whole-screen rule holds today with no
  exception. It also leaves a gap the whole-screen rule does not: a change that reveals the end state by
  styling an ancestor.
- **Refuse every set-up write that is not declared in the task file** (an allowlist of fixtures). Closer
  to complete, but each product's sign-in and client writes set-up's browser sends as it loads would have
  to be listed per task; the rule against the found name closes N3's class with no list to keep, and the
  finds rules refuse a pre-filtered list whatever wrote it.
- **A control run with no steps** (leave the start screen alone for the same time and check verify()
  fails): doubles every run's cost and is defeated by a page that waits for any key.

## What it still cannot tell

A product that answers any click by opening the record (a setting set-up turned on) shows a click that
caused the change; the finds rules refuse it on a find task (no piece typed), the blind screenshots on
any other. A product that ranks a record first because set-up read it through the API (ours' find-user
set-up looks up the shortest piece of the name that ranks the user first) is not told apart from one
that ranks it on its own merits. A change of the reference's screen no step caused is recorded, not
refused.

## Ties

No task this piece owns ties Odoo.

## Cost

No new dependency. The record now runs for every browser run (not only tasks that save) and holds the
invalidation-tracking category: a few tens of megabytes of trace events per run, parsed in the harness.
Every browser run gains the 1.1-2.1 s gap (wall time, no processor time to speak of). This round adds 27
tests to the comparison suite (21 browser runs of a stand-in, 6 on synthetic traces or rules) and 16
mutations (4 on synthetic traces or rules, 12 on one browser run each). Processor time of the
comparison unit suite on this machine: see the verify runs below.
