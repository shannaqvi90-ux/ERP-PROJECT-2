# p01 round 6: one driver process per run, sign-in pacing in the harness, ties at zero, list printing

Status: accepted (p01 builder, round 6). Extends `p01-odoo-rig-driver-sandbox.md`.

## Decision 1: every run gets a driver process of its own

Round 5 started one sandboxed driver process per harness process and ran every driver in it. A
driver could patch that process's globals (timers, `Promise.prototype.then`, a helper module) and
the patch lived on into the next run, including the other product's driver under
`--product both`: an ours driver could slow Odoo's measured part and turn a loss into a win. The
round-5 plant U5i (a patched `Function.prototype.toString`) did exactly that by accident: it broke
two later tests in the same file.

`execute()` now starts a fresh driver process (`DriverHost.forRun()`) and stops it when the run
ends. The shared process remains only to read task definitions and describe drivers; no run uses
it. Cost: about 0.1 s per run to start Node, and API sessions are no longer reused across runs
(more sign-ins, which the pacing below absorbs). Plant X1 in `test/sandbox.test.mjs` patches
timers and promises in one run and checks that the next run runs in another process, unslowed,
and that the first run's process has ended.

## Decision 2: sign-in pacing belongs to the harness process

The integration branch (3d9ff31) paced sign-ins in `lib/ours-api.mjs` and around `driver.signIn`
in the runner. With drivers in their own process, `ours-api.mjs` runs in the driver process, so its
budget would have been a second, separate one, and its 61-second wait would have run inside the
driver process. Pacing now lives only in the harness process, under one budget
(`lib/sign-in-limit.mjs`):

- the runner paces a driver's browser sign-in around the `signIn` hook. On a 429 it closes the
  context, waits until the abandoned hook (still waiting for its working screen) has ended on its
  closed page, waits out the window, then calls the hook again with the new page's handles, so the
  abandoned attempt can never act on the new page;
- the fetch bridge (`lib/sandbox/bridge.mjs`) paces a driver's API sign-in and resends it after a
  429;
- the sign-in address is declared by the product (`signInLimit` in `lib/config.mjs`; ours only),
  so test stand-ins and Odoo are not paced;
- time spent pacing inside `verify()` is reported apart (`paced_seconds`) and not charged to the
  pass, so the verify meter does not mistake the harness's own wait for a driver waiting for the
  end state.

The product's limit is unchanged. `test/sign-in-limit.test.mjs` covers both paths with a stand-in
product that answers 429.

## Decision 3: a tie at zero is reported plainly; the rule is unchanged

A metric on which both products score 0 is a tie, and a tie is a loss (gauntlet/goal.md), so
such a task cannot be won on that metric. The tie rule is the owner's: the harness applies it as
written and reports the case plainly (`tie at zero` outcome, `ties_at_zero`,
`tie_at_zero_note`, `loss_only_from_ties_at_zero`, a `TIE AT ZERO` line from `run.mjs`). Whether
such a metric should count is put to the owner as a human gate.

## Decision 4: print-list-arabic

p06's spec names "print a list report in Arabic". Odoo Community has no printed table of a list;
its nearest feature selects the listed records and prints their own document into one PDF
(Purchase > Print > Purchase Order). The task starts with a user who already works in Arabic (a
rig fixture user, `arabic.reporter`, created by set-up when missing), narrows the purchase orders
to the dataset vendor (27 orders) and prints them. Each order prints in its vendor's language, so
set-up gives the partners of those orders Arabic and clean-up restores them. Our product has no
purchase orders yet: its driver prints the users list narrowed to a search (the stand-in list it
has: 53 users of the shared dataset match the search) through the list's own Print or export menu.
The driver reads the menu's words from the product's Arabic resource file rather than repeating
them.

A screenshot that Chromium fails to capture on a loaded machine is taken again (twice at most);
any other failure still ends the run.
