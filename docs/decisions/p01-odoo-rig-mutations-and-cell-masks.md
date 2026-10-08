# p01: the instrument's mutation check is part of the one command; masks follow the cells

Date: 2026-10-07 (round 7)

## Context

The round 6 critic planted faults in the measuring instrument itself (no greyscale, no freeze at
the clock, no click refusal, no socket lock) and four of the six went unnoticed by the harness's
own tests. Round 7 added `scripts/mutations.mjs`, which removes or weakens each defence in turn in
a scratch copy and runs the self-test that must catch it, but it was a separate command a critic
had to know about.

The p02 critic (round 4) also saw list rows in the blind shots that looked misaligned around a
masked company name.

## Decisions

1. **`./erp verify` runs the mutation check** right after the harness's unit tests, in the web
   stage. A missed mutation fails the stage, so the one command fails. It takes about 80 seconds
   (one browser per mutation). `test/ratchet.test.mjs` keeps the number of mutations at or above
   `compare.instrumentMutations` in `gauntlet/ratchet.json`, and checks that every mutation still
   finds the text it changes, so a defence rewritten without its mutation is caught in the unit
   tests already. Each mutation's self-tests first run unmutated (the control) and must pass, so
   a test that fails for another reason (no browser, a broken copy) never counts as a catch. The
   runs ask for TAP output by name: the toolbox's Node 24 prints its spec reporter by default even
   to a pipe, and the first verify with the check read no results at all and reported all 16
   mutations missed.

2. **A masked name inside a clipping cell is painted over by the cell.** Playwright's screenshot
   mask paints the bounding box of the element that holds the text. In a list cell that cuts a
   long name short with an ellipsis, that box is wider than the cell, so the paint ran into the
   next column; a short name left the rest of its cell showing. The harness now looks for the
   nearest ancestor that hides its overflow and is no taller than two lines of the text (a cell,
   not a scrolling list or a bar) and paints that box instead. Every masked cell is painted edge
   to edge, so the paint lines up with the columns and the length of the hidden name no longer
   shows. The same rule applies to both products.

## Alternatives not taken

- Drawing our own overlay boxes clipped to the visible part of each element: it adds elements to
  the product's page during measured shots in a second way next to Playwright's own mask, and
  would need its own self-tests; the cell box gives the same result for the cases seen.
- Running mutations only on demand (`npm run mutations`): a check nobody runs does not keep the
  bar from moving down.
