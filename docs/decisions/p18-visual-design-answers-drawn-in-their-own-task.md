# p18: request answers are drawn in the task that delivered them

Status: decided in round 4 (serial-26). Date: 2026-11-25.

## The fault

The comparison harness's health check judged `import-5000` INVALID in about 1 of 5 runs on verify's
clean stack ("the page changed what it shows without a counted step causing it ... a task of no
traceable origin"). Traces of 20 runs (`COMPARE_TRACE_DIR`, `screenCauses` on the saved record)
showed the cause in every page-side case: a state update made in a promise continuation after a
fetch is a default-lane update. React draws it in a scheduler task posted through a message channel,
a few milliseconds after the task that delivered the answer. When a harness DevTools command (the
preparation of the next click) ran between the two, the screen change followed a task no step caused.
The first failures: the import dialog's field list (`ie-hint`, drawn 36 ms after the answer of
`/fields`), and the contacts list's tab counts, one render per answer.

## Choice

- `web/src/kernel/commitNow.ts`: `commitNow(update)` wraps `flushSync`. State set from the answer of a
  request (list definition and views, list rows, the opened record, tab counts, the import dialog's
  fields, upload, mapping, run and polling) is committed in the task that delivered the answer. Layout
  and passive effects of a synchronous commit run at its end, so the chain of renders that follows an
  answer no longer spans tasks.
- Tab counts of a list are asked together and drawn once (one update, not one per answer).
- `ListView` takes its width measurements (list body, frame, open record's content) in a layout effect
  after every render, as well as in its observers. State that already holds the measured value is not
  set again, so an observer callback finds nothing to write. Observers remain for outside changes
  (window size).
- Considered and not done: a global patch of React's scheduler (would change every screen and the
  tests), `startTransition` (does not make the render synchronous).

## Proof

On verify's clean stack recipe (generated users, no comparison dataset; `./erp up` with the seed
variables of `verify_stack_and_e2e`, seed volume 2000, after the full Playwright suite, 180 passed):
built-ours health runs of `import-5000`: 10 of 10 verified; `export-filtered-list`: 10 of 10 verified
(on a fresh stack: the task needs a Government tag in the first rows, which earlier imports hide).
Before the change, 3 of 13 import runs were invalid. One more invalid run seen in the middle of the work
(before the tab counts were drawn once) was a hover state of the harness's own pointer move on step 1
(`StyleRecalc` of `:hover` after a DevTools mouse move), which is the harness's, not the page's.

## Cut text in an inner span

The ellipsis sits on a span inside the cell, which the td does not show as overflowing. `overflowTitles`
now lists `td span, th span, [role=gridcell] span, [role=columnheader] span, dd span, li span` as
candidates, so the element that overflows gets the title (and the cell, when it is cut itself). The
Playwright check (`design.spec.ts`) looks at the same inner spans and visits the companies, branches and
numbering lists. Unit test: `overflowTitles.test.ts` "titles the span inside a cell".

## DateInput

`typedDate` keeps a typed separator from being added twice after the slash that came by itself, and
turns `20/27/-` into `2027-`. A year-first date whose last two digits could be a month (2006-...) is
read as day and month; the calendar takes it.

## Cost

No new dependency. Each ListView render reads layout once more (two property reads on the frame and the
body); rows are virtualised, so the cost is small. Processor time added to verify: the new unit tests
only (about 1 s).

## Comparison tasks

No task changed. `switch-company`, `edit-and-save` and others keep their r3 results (the code paths
changed here do not add steps or keystrokes).
