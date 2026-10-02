# p01 — Comparison harness: one operator for both products, KLM, blind shots, a tie is a loss

Date: 2026-10-02. Piece: p01-odoo-rig. Status: accepted.

## Decision

- **Node + playwright-core 1.56.1** (Apache-2.0, no dependencies), plain ES modules and `node:test`.
  It drives the Chromium preinstalled for Playwright and never downloads a browser. No test runner
  or assertion library beyond Node's own.
- **One instrumented operator** (`lib/operator.mjs`). Drivers may act only through it, so a click,
  a key chord, a field entry and a file pick are counted identically in both products. Waits for
  the product are recorded as system wait. Screenshot time is taken out of the clock.
- **Keystroke-level model** with Card, Moran & Newell's published operator times (K 0.28, P 1.10,
  B 0.10, H 0.40, M 1.35 s) and one simple placement rule set applied to both products. System
  response is measured, not modelled, and reported separately, so a slow product cannot hide
  behind the model.
- **Start state**: signed in through the product's own session outside the measurement, on the
  screen the product shows after sign-in. **End state**: the task's result visible on screen;
  then confirmed through the product's back end (not timed), so a driver cannot "pass" by
  reaching a screen that did not save anything.
- **Blind screenshots**: branding painted over by selector and by brand word, greyscale rendering,
  neutral title and favicon, random file names, a key file kept beside (not inside) the shots, and
  a review page that shows A and B at random per task.
- **Verdict**: ours must be strictly lower than Odoo on steps, keystrokes, machine seconds, human
  seconds and human-plus-wait seconds. A tie is a loss; an unbuilt or failed run is never a win.
- **Baselines**: median of three runs per task (machine seconds vary), one baseline per task under
  `gauntlet/reference/odoo/` (the owner keeps `bar/reference/` for approved captures).
- **Fairness choices recorded with each driver (`path`)**: Odoo's shortest Community path is
  used even when it is long; where Community lacks a feature the nearest Community feature is
  used and named (properties instead of Studio fields; purchase two-step approval instead of the
  Enterprise Approvals app). Odoo's right-to-left stylesheet is built once before the language task
  (a deployed server has long built it), and the F5 Odoo needs to show the right-to-left layout is
  counted as a step.
- **Dataset**: deterministic generator (seeded PRNG) for both products; generated files are not
  committed, the generator is.

## Why

- Counting through one operator is the only way the numbers mean the same thing on both sides.
- Back-end verification keeps the instrument honest when a critic writes a new driver.
- The harness lives in `./erp verify` (unit tests, ratchet `suite.compareTests` and `compare.*`
  minimums) so it cannot silently rot; the live rig checks run with `npm run test:live` because a
  clean clone has no Odoo rig.

## Rejected

- `@playwright/test` as the runner: its fixtures and retries hide timing and add a second clock;
  the harness needs one measured clock per task.
- Image-diff blinding (blur everything): it hides the very density and layout the reviewers judge.
