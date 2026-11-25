# p01-odoo-rig round 15: the health-check fault (serial-26)

Status: decided 2026-11-25. Owner of the harness side: p01. Page side: p18 (nothing needed, see below).

## The report

Since the p18 merge (89697e9e) the e2e stage's built-ours health check judged import-5000, open-attached-file
and export-filtered-list INVALID: "action outside the clock (set-up): the page changed what it shows without a
counted step causing it ... StyleRecalcInvalidationTracking: a task of no traceable origin ... after a page
task no step caused: a DevTools command of the harness during step N". Only on verify's clean stack.

## Which is true: (a) the harness mis-attributes, or (b) the page changes on its own?

(a). Reproduced on a clean verify-style stack (generated users, no comparison dataset), recording the page's
trace (COMPARE_TRACE_DIR) on the first run: import-5000 and open-attached-file invalid, the others valid.
Two different changes, both the browser's own follow-up of a counted step's input, both judged "no traceable
origin" because the heuristic looks at the page task before them, and that was a read command of the harness
during the click step (which causes nothing, by design):

1. open-attached-file, 0.048 s into the measured part. The record: a frame task
   (`proxy_impl.cc:ScheduledActionSendBeginMainFrame`) that begins with
   `StyleRecalcInvalidationTracking reason=PseudoClass extraData=:hover nodeName=BUTTON`, then dispatches
   pointerover, mouseover, pointermove, mousemove outside any script, then renders. Two tasks of the input queue
   (`main_thread_event_queue.cc:PostTaskToMainThread` and `:SetNeedsMainFrame`) ran just before it, after the
   mojo `blink.mojom.WidgetInputHandler` messages from the browser. This is step 1's pointer move (the step
   began at 0.001 s): Chromium aligns moves with frames and sets :hover there. p18's buttons have `:hover` rules,
   so the move now invalidates style at all; before p18 nothing depended on :hover and nothing was recorded.
   The existing rule counted a pointer move as input only in a task of the input queue (a frame also delivers
   the boundary events of a pointer the page moved content under, which are not a step's).
2. import-5000, 1.472 s into the measured part. Task `document.cc:ClearFocusedElementIfNeeded`: the click
   handler (a 210 ms task) disabled or removed the focused button; Blink posts a task that clears the focus,
   which records `:focus` and `:focus-visible` invalidations and dispatches blur and focusout. The task
   that posted it is the click's; the record linked nothing, so the rule guessed from the task before it.

No timer, observer or message of the page was involved in either: the page does not change on its own, so
nothing for p18 to change. (The ResizeObserver and MutationObserver of ListView.tsx were not the cause here.)

## Decision

Make the attribution exact, never wider than the trace proves (rule 9: the instrument may become more exact,
never more lenient):

- Trace category `toplevel.flow` is recorded. Chromium writes a flow start inside the posting task and a flow
  end inside the task that ran it (`SequenceManager PostTask`, same id). `analyseTrace` pairs them
  (an id seen twice links nothing) and sets `task.postedBy`. A task posted by a task of the page's own main
  thread is judged by the poster at the point where it posted (`postedCause`): a poster a step caused makes
  it the step's; a poster no step caused (a timer armed before the clock, a command that only reads) makes it
  uncaused, with the poster's reason. Posters on other threads (compositor, network, browser) link nothing and
  the old rule applies. This replaces a guess by timing for these tasks.
- `pointerStateCause`: a `:hover/:active/:focus/:focus-visible/:focus-within` PseudoClass recalculation outside
  any script, in a frame task that then dispatches pointer moves outside script, is the step's only when a task
  of the input queue ran after the previous frame (the browser handed the page input). The step is the latest
  one that began before that queue task. A pointer the page moved content under has no such queue task;
  the content's change is judged where it was made. The recalc must precede the dispatch of the moves.
- Nothing else was loosened: no timing window, no widening of the "followed at once" rule, harness read
  commands still cause nothing.

## Tests

- test/causation.test.mjs, "round 15" (synthetic traces): flow from a caused task, from an uncaused timer,
  from a read command, a flow id used twice, another flow id; the hover with and without an input-queue task,
  with an older queue task, inside script, before every step, another kind of change, after the dispatch.
- test/screen-causes.test.mjs: control (a click on a control that removes itself and shows the record,
  with :hover and :focus-visible styles verifies, all changes caused); plant U1 (the page removes its focused
  control and shows the record on its own timer: invalid); plant U2 (the page's timer resizes a box, its
  ResizeObserver shows the record: invalid). Every existing plant still passes (causation.test.mjs 51 of 51
  before the new tests).
- Mutations M135-M138 (scripts/mutations.mjs): the flow link removed, a flow from an uncaused poster accepted,
  the input-queue evidence removed, the order of recalc and dispatch ignored. All caught.

## Evidence

Health runs of the built ours drivers on a clean verify-style stack (a new stack per run): see
the notes of the round (10 of 10 valid). Before: import-5000 and open-attached-file invalid on the first run.

## Cost

The trace now carries the flow events (a few thousand per second of recording); recording is limited to
measured tasks that save and to screen-cause judgement as before. No change to the processor time of verify
beyond the comparison-harness stage.
