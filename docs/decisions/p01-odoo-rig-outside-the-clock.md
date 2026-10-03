# p01 — Nothing outside the clock acts inside it; no self-declared shortcuts (instrument 4)

Date: 2026-10-03. Piece: p01-odoo-rig (round 4). Status: accepted.

## Context

The round 3 critic planted seven faults in the measuring instrument and all seven ended
"verified" with lower counts:

- **U1, U2:** page script installed outside the clock (`page.evaluate` during sign-in,
  `context.addInitScript` during set-up) kept acting after the clock started and did most of the
  task: 2 steps instead of 5, and 1 step.
- **H2:** the start state was never checked. A find-user driver's sign-in opened the users list
  and typed the name, so 1 step was measured instead of 4.
- **T1:** screenshot time inside the clock was subtracted, so screenshots taken in a loop while the
  product worked hid its latency (0.126 s measured for a 1.5 s answer).
- **S1:** `op.type('y\n')` pressed Enter inside one field entry, so Enter was never a step.
- **K1:** `chain: true` (no mental preparation) was the driver's own claim, so declaring it on
  every step cut human seconds from 10.11 s to 6.06 s.

Later critics write the ours drivers, so each of these lets a careless or gamed driver turn a loss
into a recorded win.

## Decisions

1. **The runner owns the start.** After `setup` and `signIn`, the runner keeps only the session,
   closes the set-up browser context, and opens the start screen itself in a fresh context
   (`lib/start.mjs`). Every page, init script, exposed function, route and listener from set-up is
   gone, whatever the guard missed. A task declares `startAt`:
   - `home` and `sign-in` load the product's own address. A driver cannot choose the screen.
   - `record` and `list` reload the screen sign-in opened, from its path alone. A query or fragment
     makes the run invalid, because it would carry state typed before the clock.
   Only the cookies are kept. A signed-out start (`sign-in`) also keeps local storage, where a
   returning browser remembers the sign-in. That memory is the product's own behaviour and is
   what that task measures. For every other start, what the product remembered in the browser
   during set-up (a typed search, recent records) is dropped.
2. **The start state is checked and recorded.** After the product is ready and has no request in
   flight for 300 ms, the runner reads the screen. On `home` and `list` no field may hold typed
   text. On `sign-in` no password may be filled in, and the only remembered text allowed is the
   task's own sign-in. Any other value makes the run invalid. Each result records `start_state`
   (path, filled fields, focused element).
3. **A task already done before the clock is invalid.** `verify()` runs once on the start screen,
   read-only. If it passes there, set-up did the task.
4. **Page script is refused in every phase**, set-up included. This covers `evaluate`,
   `waitForFunction`, `addInitScript`, `exposeFunction`, `route`, `setExtraHTTPHeaders`,
   `dispatchEvent`, `setContent` and the page clock. It is enforced at run time (the guard) and in
   source (the driver lint). Drivers read with `ctx.read` and wait with `ctx.until`. Both run their
   function inside the same sentinel as `op.waitFor`, which now also cancels a navigation the
   function starts. A refusal at any point invalidates the run, even if the driver caught it.
5. **`verify()` only reads.** Page actions are refused. From the back end only reads go through:
   GET, a fixture sign-in, and Odoo methods on a read list (`search_read`, `read` …). Otherwise
   `verify()` could finish the task after the clock.
6. **Screenshots stay on the clock.** Excluding screenshot time cannot be made safe: a screenshot
   taken while the product works would always hide that work. So shot time counts. Each task now
   declares its `moments`. While measuring, a driver may shoot only those, each once, and must
   shoot every one, so both products pay for the same shots. A moment the task does not declare
   is refused, and so is a declared moment left unshot. This also closes the blindness finding
   that the number of shots per task differed by product.
7. **Continuation is derived, never declared.** Passing `chain` to any counted action is refused
   (`RefusedClaim`). `lib/klm.mjs` derives it from the recorded steps:
   - typing right after a click on the same text field, or right after a key step;
   - Enter right after typing or after an arrow key;
   - the same navigation key repeated;
   - Ctrl+A right after a click or Tab;
   - the file choice right after the click that opened the file dialog.
   The operator records whether a typing step went into the field the previous click hit
   (`same_field`). The baseline check recomputes human seconds from the steps with the same
   rules, so a `chain` flag written into a result changes nothing. Counted actions accept only a
   label. Playwright options that act uncounted (`modifiers`, `clickCount`, `force`) are refused.
8. **`op.type` takes printable text only.** Control characters (`\n`, `\r`, `\t`, `\b`, C0 and C1)
   are refused. Keys are pressed with `op.press`, which counts them.
9. **Instrument version 4.** All 20 Odoo baselines are re-captured (median of three runs).
   `test/baselines.test.mjs` rejects any baseline from an older instrument.

## Also in this round

- **Driver health check in `./erp verify`.** After the end-to-end tests, every built ours driver
  runs against the clean stack (`run.mjs --task built --product ours --health`), and verify fails
  if one no longer verifies. In round 3, p05's selection column broke switch-to-arabic and nothing
  noticed. The clean stack has no comparison dataset, so in a health check a driver's set-up may
  create the one dataset record its task needs. Health results are not comparisons.
- The switch-to-arabic ours driver ignores label-less column headers (the selection checkbox), and
  now requires at least one labelled column.
- The see-and-rerun-job reference driver waits for the list to load before it types. It then
  checks that the search box holds the whole name (round 3: Odoo dropped the first characters
  under load).
- Blindness: the demo data's company names and the database and tenant codes are masked in both
  products. Shots of a task with several expert paths come from the path that is best on the most
  metrics (`screenshots_path`), so the shots and the counts describe the same path.

## Plant tests (all must end invalid, or fail, never verified with lower counts)

In `test/guard.test.mjs`:

- U1, U1b: page script installed during sign-in.
- U2, U2b: init script, exposed function, route, extra headers or page clock installed during
  set-up.
- U3: typed text from sign-in does not survive.
- H2: a field the product refills from what sign-in typed.
- H2b: a start address with a query.
- H2c: a task done by set-up.
- V1, V2: `verify()` that acts on the page or writes through the back end.
- S1: newline in `op.type`.
- K1: declared chain.
- T1: screenshots in a loop, a declared moment taken early (its time stays on the clock), a
  declared moment never shot.
- The real ours drivers on stand-in screens: sign-in with the critic's K1 diff applied, and
  find-user with the critic's H2 diff applied.

In `test/klm.test.mjs` and `test/operator.test.mjs`: derivation rules, refusal of a chain flag,
refusal of control characters, and shots counted on the clock.

Two round-3 operator tests asserted the old behaviour, that screenshot time is not counted. They
are replaced by tests of the stricter behaviour: shot time stays on the clock, and only declared
moments may be shot, once each. The guard test "outside the measured part the same guarded page
does everything" is replaced by "set-up may act on the product but never run page script;
verify() only reads". The test count did not go down.

## Residual risk

Set-up may still change server-side state through the product's API (fixtures need it). The
pre-clock `verify()` catches a task that is already done. It does not catch a set-up that only
makes the task shorter, for example a saved default filter. Critics review the `setup` of each
ours driver. Each result records its fixtures' effect on the start screen in `start_state`.
