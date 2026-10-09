# p02 — Enter in a list's search box waits for the rows of what was typed

Date: 2026-10-09. Piece: p02-tenancy (a minimal fix outside the piece, in the shared list
kernel, made because it failed this piece's full verify). Status: accepted (round 8).

## Context

This round's `./erp verify` failed in the built-driver health check: find-user's `enter` variant
typed "Majid Anil Pillai", pressed Enter and waited 20 s for the user's record, which never opened;
the address stayed on `/identity/users?q=Majid+Anil+Pillai` and every other variant of the same task
verified (kept run: `.verify-failed/20261009T081928Z-1381621/ours-health`). The search box's Enter
acted at once when the typed text was applied and no read was flagged as running. But the list's
read starts in an effect: in the render in which the applied search gives a new query, `loading` is
still false and the rows (`loadedKey`) are still the previous query's. An Enter landing in that
window (Enter came 0.15 s after typing, right when the search-as-you-type delay fired) acted on the
old rows: not ranked for this query, more than one, so it moved to the grid instead of opening the
best match.

## Decision

- `searchEnterWaits` (kernel/lists/model.ts) is the one rule: Enter waits for the answer when the
  typed text is not applied, a read is under way, or the rows loaded are not the current query's
  (`loadedKey !== expectedKey`, the same test the list already uses for `aria-busy`). The existing
  "act once the results are in" effect then opens the best match.
- Model unit tests cover the three waiting cases and the settled case. A component test cannot put
  a key press between React's commit and its effects (testing-library flushes both), so the race
  itself is covered by the health check of `./erp verify`.

## The create-company-branch driver (lead's routing, p01 round 6)

The driver errored in p01 round 6 because the companies screen re-read a company it had just
created and remounted the branch line, losing what was typed. The forms-kernel fix (79792c5: a
record just created is not read again) is on the integration branch. This round the driver
verified 8 of 8 runs on a quiet stack, 12 of 12 runs of the same keyboard path with the browser's
processor throttled six times and every API answer delayed at random by up to 1.5 s, and in this
round's verify health check after the end-to-end suite. No driver change was needed.

## Processor time

Three model unit tests (milliseconds). Nothing else is added to the verify.
