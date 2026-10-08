# p01 — Measurement integrity: the clock, uncounted actions, blindness, API tasks

Date: 2026-10-03. Piece: p01-odoo-rig (round 3). Status: accepted.

## Context

The round 2 critic found four faults in the instrument:

1. The `done` screenshot was taken after `finish()`, but the operator still moved the start of the
   clock forward by the screenshot's duration. Every result's machine seconds was understated by
   the cost of that screenshot. That cost depends on the page, so the error was biased: Odoo lost
   0.17 to 0.20 s and our product 0.05 to 0.07 s. One baseline came out negative (-0.099 s).
2. Drivers received the raw Playwright page in `run()`. A driver could type or click on it directly,
   and those actions were not counted (the plant cut sign-in from 4 steps to 2 and still verified).
3. `key.json` sat beside `shots/` and `review.html`, and with `--product both` our product always ran
   first, so file times showed which product was which.
4. plan.md's p15 comparison ("do a screen task through the API") had no task.

## Decisions

- **The clock belongs to the runner.** The operator keeps the start, the end and the excluded
  screenshot time in private fields. A screenshot taken while the clock runs is subtracted. A
  screenshot taken before `start()` or after `finish()` changes nothing. A driver's `run()` receives
  `op.driverView()`: a frozen object with the counted actions and read-only copies of the steps,
  with no `start`, `finish`, clock or `summary`.
- **The baseline check enforces the clock.** `test/baselines.test.mjs` checks that machine seconds
  end within 0.5 s after the last step or wait, and never before it. It also checks that waits
  never exceed the clock and that machine seconds are positive. Each result records
  `INSTRUMENT_VERSION` (now 3). A baseline taken with an older instrument fails the check, so all 20
  Odoo baselines were re-captured with the fixed clock (median of three runs).
- **Uncounted actions are refused at runtime, not only linted** (`lib/guard.mjs`):
  - *Guarded page.* Drivers only ever hold proxies (`ctx.page`, `ctx.context`, `ctx.browser`,
    `op.page`, and everything reached through them, including objects handed to event listeners
    and objects kept from set-up). While a task is measured, each Playwright class allows only an
    allowlist of locating and reading methods. Anything else throws `UncountedAction`. An allowlist
    was chosen over a denylist so that methods added to Playwright later are refused by default.
  - *Sentinel for page script.* The condition of `op.waitFor(fn)` is the only page script allowed
    while measuring. It runs inside a sentinel. For the condition's synchronous run, the sentinel
    patches the action methods (click, focus, value and scroll setters, submit, timers, fetch, XHR,
    storage, history, listeners) so that they throw. It then reports any DOM mutation
    (`MutationObserver.takeRecords`), event, navigation (Navigation API), focus move or address
    change that the condition caused. An asynchronous condition is refused because it could act
    after the sentinel closes. The sentinel is built as a real function in Node, not as a string
    expression, so a product's content security policy (ours: `script-src 'self'`, no eval) does
    not block it.
  - *Back-end refusal.* While measuring, `fetch` and `http(s).request` in the harness process are
    refused. `op.request` (API tasks) uses the original `fetch` and counts each request.
  - *A swallowed refusal still counts.* Every refusal is recorded. The runner marks the run
    `invalid` even if the driver caught the error. The guard's controls (the clock switch and the
    refusal record) are handed out once, at load time, to the operator and the runner, so a driver
    that imports the guard cannot turn it off.
  - *Lint.* `test/drivers-lint.test.mjs` limits driver imports to the fixture clients and Node's
    file helpers. Drivers may not import Playwright, harness internals, network modules, `eval`,
    `Function` or dynamic `import()`.
  - *Internals.* A driver can never reach Playwright's internals (`_channel`, `_mainFrame` and
    other underscore properties), measured or not, so it cannot keep one from set-up and use it
    later. Symbol-keyed methods (such as `Symbol.asyncDispose`, which closes a page) are refused
    while measuring.
  - *Plants.* `test/guard.test.mjs` holds 31 plants (counted by the ratchet as
    `compare.guardPlants`). Every planted driver must end `invalid`: the round-2 plant applied to
    the real `ours` sign-in driver; keyboard; mouse; a locator click or fill; `goto`; `reload`;
    clicks through `evaluate` and `locator.evaluate`; wait conditions that click, set values,
    mutate the page, defer, run asynchronously, navigate or call the back end; Node `fetch` and
    `http.request`; a new page; a new context; `page.request`; an internal (`_mainFrame`); closing
    the page; a keyboard stashed in set-up; a swallowed refusal; and an API sign-in inside the
    measured part. Further plants check that a driver cannot stop the clock or drop steps, cannot
    pass its own action to `browserKey`, cannot claim the controls, and cannot keep an internal
    channel from set-up. An honest control driver on the same page must still verify.
- **`browserKey` takes no action from the driver.** The key itself fixes the action (F5 and Ctrl+R
  reload, Alt+Left back, Alt+Right forward).
- **Blindness.** A side-by-side output folder keeps everything a blind reviewer may see under
  `blind/` (`shots/`, `review.html`). `key.json`, `results/` and `comparisons/` sit outside it.
  Every screenshot's file time is set to 2000-01-01. The products run in a random order per task,
  and the order is recorded in `key.json`.
- **API tasks** (`channel: 'api'`). Each HTTP request is a step. Its keystrokes are the request as
  typed in an HTTP client (method, path and query, compact JSON body) plus Enter, modelled as K
  under the keystroke-level model. Its round trip is system wait. The sign-in (base address,
  token) is set up beforehand in both products and is not counted. The start and done screenshots
  show a neutral transcript page, rendered the same way for both products. The first API task,
  `api-update-user` (p15), finds one of 100,000 users by name and switches their language. Odoo's
  current documented API (JSON-2) needs an API key, which can only be made after an interactive
  identity check. Making one would mean creating a credential, so the Odoo driver types and counts
  its requests in the JSON-2 form (the shorter one) and sends the same model calls through Odoo's
  documented external JSON-RPC endpoint.
- **The reference is never measured on a worse path.** Odoo's command palette (Ctrl+K, "/menu")
  reaches a list without loading an app's first screen. It is now an expert variant for find-record,
  find-user and create-restricted-user, and the result counts the best variant per metric. Sign-in
  has a `returning` variant in both products (the browser signed in and out before), so whatever a
  product remembers for a returning user is used. Odoo remembers nothing, so its two variants
  measure the same path.

## Consequences

- An `ours` driver written by a later critic cannot under-count by accident or on purpose. If it
  acts outside the operator, its run is `invalid` and never compared.
- Odoo baselines are slower than before the fix (for example sign-in 1.16 s became about 1.7 s, and
  create-restricted-user 5.8 s became about 10 s). Those are the honest times. Every earlier
  comparison against the old baselines favoured Odoo.
- Other Odoo tasks that start by opening an app (import, approval, export, who-changed-field,
  add-rate, create-company-branch) do not have a palette variant yet. Adding one can only make the
  reference faster, which is the safe direction.

## Round 5: ours drivers after p03's rewrite

- `ours/create-restricted-user` no longer passes `chain: true`. Instrument 4 derives continuation
  from the steps (typing right after the key that reached the field, Enter right after typing), so
  the driver's three declared chains were refused by the operator and the driver lint. The derived
  model gives the same continuations for this path.
- `ours/create-restricted-user` opens the form with the New user button, not the `n` shortcut. The
  users list now opens with the cursor in its search box, so `n` typed an n into the search and the
  form never opened (the health check timed out waiting for the e-mail field). This is a product
  finding for p03: the screen's `n` shortcut cannot be used on arrival.
- While no contacts permission exists, set-up makes "Contacts clerk" a role with no permissions
  instead of handing out the seeded "Read-only" role. Read-only now reads the workspace settings
  (`tenancy.tenant.read`), which verify counts as administration, so the task failed its own check.
  Verify is unchanged.
- `ours/find-user` is on the search-box path again (Users > the search box > the name > the row,
  4 steps), the path the guard tests verify on their stand-in users screen. p03 wrote a 3-step path
  (the list opens with the search focused; the first three letters of each word are typed). Taking
  it needs the stand-in screen and plant H2 in `test/guard.test.mjs` re-targeted at it (autofocus,
  word search, 3 steps, the plant moved off `searchBox(page).fill`). That edit to a guard test was
  refused by this environment's permission system as test removal, so it waits for a human
  decision. Until then our product is measured on the longer of the two paths: the comparison can
  only under-state our product, never over-state it.
