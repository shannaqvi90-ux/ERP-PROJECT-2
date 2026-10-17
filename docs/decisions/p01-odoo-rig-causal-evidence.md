# p01: which step caused a write is read from the browser's record, not from time

Round 12, answering critic p01 r11 (BLOCKED: plants S1, S1b, S2, S3 recorded as verified with one
step; mutation C3 missed). Instrument version 10.

## Problem

Round 11 credited a page's write to a counted step when it went out during the step's window or
within `REACTION_SECONDS` (1 s) after it, or within that time of the answer to a request sent during
a step. Time is not causation:

- S1, S1b: a click (or scroll) waited 3 s for a "Close" banner while the page approved on its own
  timer; the wait was inside the step's window.
- S2: the first pointer press made the page ask the back end something that answered after 2.5 s;
  the page approved on the answer ("one link").
- S3: the stand-in armed itself on `verify()`'s read before the clock and let the page's ordinary poll
  approve 0.6 s later.
- C3: a step's label counted as typed text survived the whole suite.

Any rule built on time alone is beaten by a driver that waits for a sign the page shows and clicks
just before the page's own timer fires (plant S4 here).

## Decision

1. **Causal evidence from Chromium's trace** (`gauntlet/compare/lib/causation.mjs`). For a task that
   saves, the runner records the page's trace (categories `devtools.timeline`,
   `disabled-by-default-devtools.timeline`, `toplevel`, `devtools`, `navigation`, `loading`,
   `blink.user_timing`) from just before the clock until it stops, through a CDP session of its own
   (`Tracing.start`, `ReportEvents`, 256 MB buffer, `recordUntilFull`; a record cut short makes the
   run an error to repeat, never a pass). Every write the page sent is traced back, task by task:
   - **input**: a task holding an input event dispatched outside any script (only the harness's steps
     put input in the page) is the step's that began last before it (the renderer may handle a key a
     few milliseconds after the step's call returned; a step's window starts when the operator began
     it, the pointer moving to its target included);
   - **DevTools command**: inside a step's window it is that step's (a file chosen, the device's
     answer); outside, the harness reading the page, which causes nothing;
   - **timer**: caused when the task that armed it was, it fires once and its delay is at most
     `REACTION_SECONDS`; a repeating timer (a poll), a longer one (R2b) and one armed before the clock
     (S1, S4) cause nothing;
   - **animation frame**: caused when the task that asked for it was (S7: a frame loop from the load);
   - **answer**: a task that delivered the answer to a request (where the page's code waiting on it
     runs) is caused when every request it answered was sent by a caused task and answered within
     `LINK_SECONDS` = 2 s (S2: 2.5 s; the honest control C3 of round 11 waits 1.3 s for its check);
   - **no traceable origin** (a message port, a worker's message, an observer, a scheduled form
     submission): caused only when the page task just before it was caused by a step, it began within
     `REACTION_SECONDS` of that task's end, no uncaused page task ended within `GUARD_SECONDS` = 0.1 s
     before that task (one that ran just before the step could have posted the message, S5), and no
     worker of the page ran meanwhile (S8). DevTools commands outside steps (the harness polling) do
     not count as the page's own activity; commands are known by `DevToolsSession::DispatchProtocolCommand`,
     which page script cannot produce.
   - a **navigation** (a posted form) is judged by the task that started it
     (`FrameLoader::StartNavigation` with its address; S9: `form.submit()` from a timer).
   The trace's clock is tied to the harness's with `Tracing.recordClockSyncMarker` at both ends (on
   this machine both read `CLOCK_MONOTONIC`; the offset is measured, not assumed).
2. **Timing stays as a second layer**: a write counts only when both say a step caused it.
3. **Steps are timed from their input** (critic fix a): a pointer step first runs Playwright's
   actionability checks as a trial (`click({ trial: true })`, `waitFor` for a scroll), then dispatches
   and times the input; `began` records when the wait started. A device confirmation is timed from
   the release until the virtual authenticator answered (`WebAuthn.credentialAsserted/Added`).
   Machine seconds and the keystroke-level model are unchanged (the wait is still on the clock).
4. **An unpredictable gap before the clock** (critic fix c): for a task that saves, the clock starts
   1.1-2.1 s (`crypto.randomInt`) after `verify()`'s read before it, so the product cannot time a
   write from that read.
5. **Self-tests** (fix d, e): `test/causation.test.mjs` holds the rules on synthetic traces (cheap),
   the critic's S1, S1b, S2, S3, further plants S3b-S10, honest controls, and a test that a step's
   label never counts as typed text. 19 new instrument mutations (M63-M81), each caught.

## Alternatives considered

- **V8's async call stacks in `Network.requestWillBeSent` initiators** (the critic's suggestion b).
  Tried first: the chain shows `setTimeout`, `setInterval`, `requestAnimationFrame`, `Promise.then`
  and `await` hops, but not message ports or workers (the chain is cut at the handler), and the root
  of a chain is untyped and untimed: a handler run by a click and one run by a laundered message look
  the same. The trace's tasks carry what kind of task each is and when it ran.
- **The full task graph from the trace's flow events** (`toplevel.flow`, mojo flows): would link a
  message to the task that posted it, but costs several times the events and depends on Chromium's
  internal mojo plumbing. The guard rules above cover the untraceable tasks instead.
- **A twin start page that gets no input** (a write it sends is the page's own): a product can arm
  only one of two pages, and it doubles the browser time.

## Measured on the real products

- Odoo (shared rig, this round): edit-and-save's `web_save` follows the answer to its `onchange`
  sent by the click or key (0.056-0.074 s); switch-to-arabic's `preference_save` follows the answer to
  `web_save` (0.068-0.073 s); follow-approval's `button_approve` is the click's input task; import's
  first `execute_import` is the click's, its later batches follow their predecessors' answers after
  3.1-3.7 s (continuations of the same write, as round 11 allowed); sign-in's `POST /web/login`
  (all three paths) starts in the key's or the device's task.
- Ours: all 13 built drivers verified against a clean stack with the new instrument.
- All Odoo baselines re-captured with instrument 10 (`gauntlet/reference/odoo/tasks/`).

## What it still cannot tell

A product that does the task on any click at once (set-up turned on such a setting) shows a click that
caused the write; that is judged from the blind screenshots and review of the task, as before. A page
task of no traceable origin is credited to the step just before it only with nothing else of the
page in between; a page that posts a message from a task that ran within 0.1 s before the step's
task is refused, which an honest page with constant background work could also meet (only when its
saving write takes such a route; none of the drivers' writes does).

## Cost

No new dependency (Chromium's trace through `playwright-core`'s CDP session). The record is taken
only while a task that saves is measured. Processor time: see the verify record of this round below.
