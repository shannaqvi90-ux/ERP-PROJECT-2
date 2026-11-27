# p01-odoo-rig round 17: blind shots equal in lightness, fixture names masked, round-16 rules held to their edges

## Why
The round-16 critic found the blind screenshots not blind. Greyscale keeps lightness; our screens are dark until a person
chooses a theme (web/src/kernel/theme.ts) and the reference's are light, so all of ours' shots had a mean brightness of
13 to 28 of 255 and all of the reference's 121 to 242. In switch-to-arabic ours' top bar also showed the fixture person
"Layla Linguist", masked in the reference's shot only.

## Decisions
- **The light theme for the shot only.** `neutraliseDocument` (lib/blind.mjs, already run in the harness's own isolated
  world just before each shot) now sets `data-theme="light"` on `<html>` when the screen states another theme, remembering
  the original in `data-blind-theme`; `restoreLook` puts it back right after the shot. The reference states no theme and is
  untouched. Chosen over inverting colours or normalising lightness with a filter (a filter distorts contrast of one
  product only) and over a context-level `erp.theme` device key (it would change what the page reads, and the measured run's
  start state, which the instrument must not touch). The change is made inside the shot, which is on the clock for both
  products alike and whose window the cause analysis already excludes; nothing measured (steps, keys, seconds) changes.
- **One function, `blindScreenshot`,** takes every blind shot for the operator and the self-tests, so the tests exercise the
  production path (a self-test also fails if operator.mjs calls `page.screenshot` itself).
- **Fixture people** (`Layla Linguist`, `Sara Signin`, `Hessa Clerk`) joined the identity list (shared by both products'
  shots). A self-test reads every driver and task for fixture names (`TESTER = {..., name}`, task `input: {name}`, Odoo
  `create('res.users', {name})`) and fails on one that `revealsIdentity` does not catch.
- **Self-test for the brightness band:** test/blind.test.mjs draws a screen with the product's own tokens.css in each theme
  (dark, hc, light) and a reference-like light screen, takes blind shots, decodes them in a browser canvas and requires every
  mean above 150 and within 40 of the reference's; a control shows the dark screen alone (raw greyscale) below 60, so the
  test fails on the round-16 harness.
- **review.html keeps every task** run into one --out (follow-up 3): each task's section is kept in `review-sections/`
  beside the key and the page is built from all of them.

## Round-16 gate gaps closed (gate-backlog rows 26 and 27)
The critic's 13 mutations of the round-16 causation code, 10 missed. Seven new synthetic-trace tests in
test/causation.test.mjs ("round 17") catch every one; all are also instrument mutations M156-M164 in scripts/mutations.mjs
(plus M165-M167 for the blind rules; the mutation copy now carries web/src/design/tokens.css for the brightness test):
N2 (states widened), N3 (state credited by task end), N24 (any mark kind), N9 (earliest maker only), N10 (page script in a
command not an entry), N11 (harness-posted task laundered to the previous step), N16, N18, N19 (edges of the forcing update).
N1 (the `task.protocol` condition of inputDefaultActionCause) was dead code: pointCause judges a DevTools command's task
before it can reach that rule, so no test can tell it from its absence; the condition was removed rather than left unchecked.
With the critic's own script (`n-mutations.mjs`, restricted to the synthetic tests) all of N2, N3, N5, N8-N11, N16-N19, N24
are caught.

## Cost
No product code. Harness tests +11 (compare 499 to 510), instrument mutations 155 to 167 (most run synthetic traces; three
start one browser each). Ratchet minimums raised: suite.compareTests 510, compare.harnessTests 398, compare.instrumentMutations 167.
Estimated added processor time: under 100 s.

## Ties
No comparison task result changed this round; no tie path analysis needed beyond follow-approval (not compared: the ours driver waits for p13, as the spec allows).

## Fix after the integrator's pre-check (round 17, fixer 1): the light look is a style sheet, not a theme switch

The integrator's health check of the merge (`run.mjs --task built --product ours --health`, three runs on a clean stack)
had find-record INVALID once: "the page changed what it shows without a counted step causing it ... a task of no traceable
origin (a message, an observer) after a page task no step caused: a DevTools command outside any step". Switching
`data-theme` dark to light and back made the product page redraw; native controls change size with `color-scheme`, so its
own resize and mutation observers (ListView.tsx, `flushSync(grow)`) ran script after the shot, outside any step. The
causation rule is right to judge that: the page must not run script because of the harness.

Change: `blindScreenshot` (lib/blind.mjs) no longer writes to the page's theme at all (`restoreLook` and
`data-blind-theme` are gone). In the harness's isolated world it reads (writes nothing) the declarations of the page's
own `[data-theme="light"]` style rule, leaves out `color-scheme`, and the shot's style sheet (Playwright adds and
removes it, as for the greyscale) sets them on `:root[data-theme]` with `!important`. The reference states no theme and
gets nothing. self-test (test/blind.test.mjs) now also records every attribute write on `<html>` during a shot and requires none.
Mutations M165 and M166 were reworded to the new code (still 167 in all, all caught). Not run live: the clean-stack
health check (see the fixer's notes in the commit); the compare tests were run here.
