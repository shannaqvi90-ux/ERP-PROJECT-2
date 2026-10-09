# Blind comparison harness

Measures our product and the Odoo Community reference on the same tasks, the same data and the
same instrument, so critics can judge "better than Odoo" from numbers and blind screenshots
instead of impressions. Odoo is a reference only (CLAUDE.md rule 7): nothing here copies its
code, views or text; drivers operate its screens as a user would.

## One command

```bash
cd gauntlet/compare && npm ci                    # once (playwright-core and acorn; uses the preinstalled Chromium; Node 22.13+)

node run.mjs --list                              # the tasks
node run.mjs --task find-record --product odoo   # Odoo baseline -> gauntlet/reference/odoo/
node run.mjs --task all --product odoo --repeat 3   # refresh every baseline (median of three runs)
node run.mjs --task find-record --product both --out ../evidence/p05/r1/compare   # side by side
node run.mjs --task find-record --product ours --out /tmp/x                         # ours only
```

Our product allows 30 sign-ins a minute per client. The harness keeps every sign-in it makes under one
budget of 24 a minute and waits out a 429 (`lib/sign-in-limit.mjs`, `signInLimit` in `lib/config.mjs`):
the runner paces a driver's browser sign-in (on a 429 it closes the context, waits for the abandoned
attempt to end, waits out the window and signs in again in a fresh context), and the fetch bridge paces
an API session's sign-in from the driver process. Pacing never happens inside a measured part, and a
paced wait inside `verify()` is reported apart (`paced_seconds`) and not charged to the pass.

Options: `--task <id|id,id|all>`, `--product odoo|ours|both`, `--out <dir>`, `--repeat N`
(median machine seconds of N runs), `--headed`. Exit code 1 when a run fails, errors or is invalid;
2 on bad arguments or a reference rig short of the bar.
With `--product both` the two products run in a random order per task (recorded in `key.json`).

Tests: `npm test` (unit tests; the live rig checks run too when the rig answers),
`npm run test:live` (also drives every Odoo task on the rig, and fails if the rig is down).
`./erp verify` runs the unit tests and counts them against `suite.compareTests` in
`gauntlet/ratchet.json`.

The Odoo reference must be running: `tools/odoo-reference/up.sh` (see its README). Before any run
on Odoo, `run.mjs` checks the live rig for at least 100,000 rows in each of the seven main lists
(`lib/rig-volume.mjs`, the same check as the live test) and exits with code 2, recording nothing,
when a list is short or the rig cannot be checked. Odoo vacuums job-run rows older than a week, so
a rig seeded once falls short after about a week; `up.sh` tops it up.

## What is measured

Every driver acts only through the instrumented operator (`lib/operator.mjs`), so both products
are counted the same way. This is enforced, not trusted (`lib/sandbox/`, `lib/guard.mjs`,
plant-tested in `test/guard.test.mjs` and `test/sandbox.test.mjs`, linted in
`test/drivers-lint.test.mjs`):

- **Drivers never run in the harness process** (round 5), and **every run has a driver process of its own** (round 6):
  a driver that patched its process's globals (timers, promises, a shared helper) would otherwise slow or steer the next run
  in it, the other product's driver included (`--product both`); plant X1 in `test/sandbox.test.mjs`. Every harness process starts one driver
  process (`lib/sandbox/host.mjs`) under Node's permission model (`--permission`: no child process,
  no worker thread, no native addon, no WASI, no inspector, no `process.binding`; files may be read,
  and written only in the driver process's own scratch folder, which is its `TMPDIR`), with
  `lib/sandbox/lockdown.mjs` preloaded before any driver code: no socket of any kind (no
  connection, no listening, no UDP, no `WebSocket`), no loader hooks, no V8 flags. The driver
  process holds no Playwright object, no clock and no network. `ctx.page`, `op`, `fetch` and the
  rest are stand-ins there: every call travels over the IPC channel to the harness, which performs
  it through the guards below, judged by the phase at the moment it arrives
  (`lib/sandbox/bridge.mjs`). A fetch captured at load, a child process reached through
  `process.getBuiltinModule`, a timer left by set-up or a patched clock can therefore only ever
  produce such a request, or nothing. A refusal inside the driver process is reported to the
  harness, so a driver that swallows it still has its run marked invalid. The harness reads
  drivers and task definitions through the driver process too; it never imports one.
  `execute()` refuses a driver object handed to it directly (that would run in the harness).
- Set-up and verification reach the product through the harness's fetch: only the product's own
  address, reads only in `verify()`, nothing while the start is prepared or the task is measured.
  Playwright's own HTTP client (`page.request`) is refused in every phase (a request it left running
  in set-up would land inside the measured part); calls set-up leaves running through fetch are
  waited for before the start. An API session carries only the headers a signed-in client sends
  (authorization, cookie, `X-Erp-Request`, accept), nothing that changes what a typed request does.
- A function a driver hands to the page (`ctx.read`, `ctx.until`, `op.waitFor`) arrives as its text.
  The harness parses it (acorn, MIT) and accepts exactly one function expression, so a crafted text
  cannot close the harness's call and run page script outside it (and see "Page functions" below).
  A file the harness writes for a driver (a screenshot path, a download folder) must lie in the
  driver process's scratch folder.

- `run(op, ctx)` gets a view of the operator with the counted actions only: no clock, no
  `start`/`finish`, read-only copies of the steps.
- `ctx.page`, `ctx.context`, `ctx.browser` and `op.page` are guarded proxies, and so is everything
  reached through them (locators, keyboard, mouse, frames, `page.request`). While a task is
  measured they allow only locating and reading (`locator`, `getByRole`, `count`, `inputValue`,
  `textContent`, `boundingBox`, `url` …). A click, fill, key press, mouse action, `goto`, `reload`,
  new page or new context throws, and the run is recorded as **invalid** (never verified), even if
  the driver catches the error. A refusal at any point of the run (set-up included) invalidates it.
- Page script and request rewriting are refused **in every phase**, set-up included: `evaluate`,
  `waitForFunction`, `addInitScript`, `exposeFunction`, `route`, `setExtraHTTPHeaders`, the page
  clock … (round 3: a listener installed during sign-in finished the task inside the measured
  part). Drivers read the page with `ctx.read(fn, arg)` and wait with `ctx.until(fn, { arg })`;
  both run `fn` as a page function (below).
- **Page functions** (round 7, `lib/page-script.mjs`): the condition of `op.waitFor(fn)` and the
  functions of `ctx.read` and `ctx.until` may only read the page, now and later. Three layers, each
  plant-tested on its own (`test/page-script.test.mjs`):
  1. *The source.* One synchronous function expression that reads: no async function, `await`,
     generator, import, `with`, `this` or `debugger`; no write to any property and no assignment to
     a name it did not declare; no computed property but a number (no name built at run time); none
     of `window`, `self`, `globalThis`, `top`, `parent`, `frames`, `document.defaultView`,
     `contentWindow`, `eval`, `Function`, `Reflect`, `.constructor`, `.prototype`, `.then`,
     `Object.values` and the other `Object` members that hand out property values; `new` only for
     plain data (`RegExp`, `Set`, `Map`, `Date`, `URL` …); `location` only as `location.pathname`
     and its other parts. A `javascript:` address set from any script world runs later in the
     page's own world and fires no navigate event, so the address object must be out of reach.
     The driver lint applies the same check to every page function in a driver's source.
  2. *Its own world.* It runs in an isolated script world of the harness's (through the browser's
     debugging protocol), never in the page's own: the product's globals, prototypes and handlers
     are out of reach, so it cannot leave a hook the product calls later. Only the document is
     shared. Its argument is plain data.
  3. *Armed for good.* That world is armed when it is created and never disarmed: every method and
     property setter of the browser's interfaces that acts (clicks, focus, values, DOM changes,
     timers, promises, observers, listeners, network, storage, history, workers, animations) throws
     and is logged, and its prototypes are frozen. Whatever a function scheduled anyway would find
     every action refused when it ran, and the log is read after every call.
  Around each call any DOM change, event, navigation (cancelled), focus move or change of address
  it caused is reported. A refusal while measured is an uncounted action; the run is invalid.
  Conditions are polled every 50 ms from the harness; one that runs over 10 s is ended.
- While measured, `fetch` and `http(s).request` from the harness are refused, so a task cannot be
  done through the back end and count nothing. API tasks use `op.request`, which counts. The
  guard is installed when the harness loads. An API task's transport (how a request as typed is
  carried, for Odoo's JSON-2 form over JSON-RPC) is one of the harness's own (`lib/api-transport.mjs`),
  named in `ctx.useApi({ transport: 'odoo-json2' })`; a driver cannot pass its own, which could send
  more than it typed.
- Drivers may import only `./_common.mjs`, `lib/ours-api.mjs`, `lib/odoo-rpc.mjs`, `lib/xlsx.mjs`
  and Node's file helpers; no Playwright, no network module, no `eval` or dynamic import, no page
  script, no `chain` (lint, `test/drivers-lint.test.mjs`).
- `op.type` takes printable text only: a control character (`\n`, `\t` …) would press Enter or Tab
  inside one field entry, uncounted, so it is refused (press those keys with `op.press`).
  A paste chord is refused unless text was selected and copied (or cut) earlier in the measured
  part: the browser's clipboard outlives set-up. Chords are read as the keys they press, whatever
  their spelling (`ControlOrMeta+v`, `Control+KeyV`, `control+V`, modifiers in any order); a copy
  counts only when the operator finds a selection as the copy key is pressed (an empty copy leaves
  the clipboard as set-up filled it); and the runner empties the clipboard before the start. Counted
  actions take only `label` (and `waitFor` its timing options); any other option, `chain` among
  them, is refused.

**Phases.** Set-up and sign-in may act on the product (fixtures, signing in). Then the runner
takes over the start (`lib/start.mjs`): it keeps only the session — the cookies, and for a
signed-out start also the browser's local storage, where a returning browser remembers the
sign-in — closes the set-up browser context with everything in it, opens the task's start
screen in a fresh context itself (`startAt` in the task: `home` and `sign-in` are the product's own
addresses; `record` and `list` are the screen sign-in opened, reloaded from its path, no query or
fragment allowed), waits until the product is ready and quiet, and checks the start state: on
`home` and `list` no field holds typed text, on `sign-in` no password is filled and the only
remembered text is the task's own sign-in. Where the start landed is checked too: a `home` start
must land on the product's home (`homeLanding` in `lib/config.mjs`, so a home preference changed
in set-up is caught), and a `list` start's address may not name the task's data (a search carried
in the path). Every browser context set-up opened is closed at the start, so an action a driver
left pending there (slow typing, a delayed click) cannot finish inside the measured part; another
browser cannot be launched at all. The start state is recorded in each result
(`start_state`). Before the clock starts, `verify()` runs once on the start screen: if it already
passes, set-up did the task and the run is invalid. `verify()` only reads: page actions are refused,
and from the back end only reads go through (GET, a fixture sign-in, Odoo read methods). Clean-up
may act again.

**Set-up cannot do the task off the clock** (round 9, critic p01 r8: `verify()` got no outcome
before the clock and `run()`'s outcome after it, so it answered "not done" only before the clock,
and a run with no counted step was accepted: set-up did the task through the API, 0 steps, a
recorded win). Now, plant-tested in `test/before-clock.test.mjs` (the critic's P1, P1b, P2 and
P3-P9) and `test/sandbox.test.mjs`:

- `verify(ctx)` takes `ctx` alone, the same before and after the clock; `run()`'s outcome never
  reaches it (the lint refuses a second parameter). Each call runs in a **fresh process of its
  own** (`DriverHost.forVerify`), never in the run's: nothing `run()` keeps in its process (a
  module variable, a global, `ctx.state`) is there. `ctx.state` arrives as data, as set-up and
  sign-in left it when the start screen was ready (functions and harness objects left out).
  `ctx.downloads` lists the files the measured part downloaded (`{ file, url, name, sent }`, `sent`
  being what the page sent between the click and the file), copied by the harness into a folder only
  it writes; it is empty before the clock.
- A verify() process **reads only** the harness's code and data (`lib`, `drivers`, `tasks`, `data`,
  `node_modules`), the driver's own folder and the downloads, and **writes nothing**: not the run's
  scratch folder, not the shots and results, not `/proc` (a mark left by `run()` or a file's time
  would tell it which call it is in).
- A verify() process **has no clock**: `Date` (and every date made without a value, `Intl`
  formatting without a date), `performance.now` and `timeOrigin` (on the prototype too),
  `process.hrtime` and `uptime`, `os.uptime` and the processors' time counters stand at the moment
  the run began; the diagnostic report is gone, and the product's `Date` header is stripped from
  the answers it gets.
- **A measured part with no counted step is refused** (invalid): nothing the person did could have
  done the task.
- A task whose end state is saved in the product declares **`saves: true`** (every task whose done
  text names the back end must). Its `verify()` must read that state from the back end, and one of
  its back-end reads must answer **differently after the clock than before it, and the same in
  both passes after it** (the second pass starts at least 1.1 s after the first, so a read that
  tells the time never counts). The comparison is by what was asked (method, address, body; a
  JSON-RPC id ignored) and recorded in `saved_state`. Otherwise the end state was there before the
  clock (set-up did the task) or `verify()` never read it, and the run is invalid.
- A task that names what the person enters (**`enters`**: keys of its `input`) must show one of
  those values arriving: a read that changed must hold an entered value after the clock that it did
  not hold before (a value is found as a person would have typed it: letters and digits in order,
  separators free, a number as an equal number). And **the start screen may not already show an
  entered value** (text or field values), or the start is unfair.
- A keyboard-only task (**`keyboardOnly: true`**) fails on any pointer step (click, double click,
  scroll, file pick). The harness judges it from the steps; `verify()` no longer reports it.
- Every verify() call signs in again in its fresh process; the harness answers a fixture sign-in it
  already answered in this run with the same answer (`signIns`), so no extra session is made and the
  product's sign-in limit is not spent.

What the instrument cannot do: tell whether a driver's `verify()` reads the right thing. It can
only make sure `verify()` reads the same way before and after the clock and that, for a task that
saves, the state it reads changed during the measured part. Drivers are reviewed code.

The clock (`machine_seconds`) starts at the first measured action and stops when `run` returns,
right after its last step or wait. Round 5 closes the ways to finish a task after that:

- If a request that changes the product (a document load, or any method but GET, HEAD and
  OPTIONS that is not one of the reference's documented read calls) is still under way when `run`
  returns, the clock runs on until it ends: a save is the product's answer to the task (system
  wait "the product still answering when run() returned"). Reads still loading (avatars, a chatter)
  are not waited for. Round 7: a document still loading when `run` returns (a client that reloads
  itself after a save) is the product still answering too: the clock runs on until it has loaded
  (system wait "the page still loading when run() returned").
- When the clock stops the page's own script is frozen (no timer, scheduled render or animation
  frame of the product runs any more; reading and screenshots still work), then (round 7) whatever
  it is still loading is aborted, because freezing script does not stop the continuation of a
  request already under way (critic plant T3: a read answered 2 s later reached the screen
  `verify()` read). What was under way is recorded (`requests_in_flight_at_clock`) and its new
  requests are refused (`requests_after_clock`). The screen is fingerprinted (address, elements
  with their attributes, text, field values, focus) after the freeze and before the abort, and again
  after `verify()` (`screen_at_clock`, `screen_after_verify`): if it changed, `verify()` may have
  read an end state the measured part never showed, and the run is invalid (plant T6: a request's
  failure handler wrote the end state as it was aborted). It is thawed for clean-up.
- `verify()` reads; it never waits. Every wait is refused there (`Locator.waitFor`, `waitForURL`,
  `waitForTimeout`, `ctx.until` …) and every read times out after 0.5 s. `verify()` runs twice and
  each pass is metered (`verify_passes`): a pass that asked the harness nothing for over 1 s (it
  slept or spun), sent over 100 requests or the same back-end read twice (it polled), or a first
  pass over 3 s and three times slower than the second, waited for the end state, and the run is
  invalid.
- Back-end calls set-up left running are waited for before the start, so they land before the
  clock (where "already done before the clock" sees them), never inside it.

Everything inside the clock counts, screenshots included (round 3:
taking screenshot time out let a driver hide the product's latency behind screenshots). So that
both products pay for the same shots, each task declares its `moments`; while measured a driver
may shoot only those, each once, and must shoot every one. The `done` screenshot is taken after
the clock stops. `test/baselines.test.mjs` checks every baseline: machine seconds end within
0.5 s after the last step or wait and never before it, and the waits never exceed the clock.
Results record the instrument version (`INSTRUMENT_VERSION` in `lib/runner.mjs`, now 7); a baseline
from an older instrument fails the check until it is re-captured.

| Measure | Definition |
|---|---|

| Measure | Definition |
|---|---|
| steps | each click, each key chord, each field entry (typing a value), each file pick, each scroll; in an API task each HTTP request |
| keystrokes | each key pressed; Shift counts; a chord counts each of its keys; an API request counts as typed (method, path and query, compact JSON body) plus Enter |
| machine_seconds | wall clock from the first step to the verified end state on screen; the task's declared moment shots included |
| system_wait_seconds | the part of machine seconds spent waiting for the product to respond |
| human_seconds | keystroke-level model of the steps (operators below); system response not included |
| human_plus_wait_seconds | human_seconds + system_wait_seconds |

Keystroke-level model operator times (Card, Moran & Newell, CACM 23(7), 1980): K 0.28 s (average
non-secretary typist), P 1.10 s, B 0.10 s per press or release (a click is 0.20 s), H 0.40 s,
M 1.35 s. One M before every step except a continuation, which the instrument derives from the
recorded steps (a driver cannot declare it): typing right after a click on the field it types
into, or right after a key step; Enter right after typing or an arrow key; the same navigation key
again; Ctrl+A right after a click or Tab; the file choice after the click that opened the dialog.
No step continues one that began on another screen (round 5): each step records the address path
it began on (`screen`), and a step after one that opened a new screen starts with M (reading the
new screen is a mental step), so typing a name right after the Enter that opened a list carries an
M in both products. One H whenever the hand moves between mouse and keyboard. Details and the exact rules:
`lib/klm.mjs`.

Start state, for both products: the task's `startAt`, opened by the runner (see Phases above);
usually signed in, on the screen the product shows right after sign-in. End state: the task's
"done" on screen, then confirmed through the product's back end (not timed, read-only).

Verdict per task (`comparisons/<task>.json` for `--product both`): ours must be strictly lower on
every measure. **A tie is a loss.** An unbuilt or failed run is never a win.

**Both at 0 on a count (owner decision, 2026-10-08, needs-human #11; gauntlet/goal.md bar item 2).**
A count metric (`steps`, `keystrokes`) on which both products score exactly 0 is left out of the
task's comparison: neither a tie nor a win. Only exactly 0 on both sides (0 against anything else
is compared as usual), only count metrics (a time equal on both sides still ties, even at 0). Every
other metric must still be strictly lower for ours; any other tie is a loss; when every metric
ties, or nothing is left to compare, the task is a loss. The comparison names the metrics left out
(`left_out`, `left_out_note`, and each one's outcome reads `left out (both exactly 0)`), and
`run.mjs` prints them.

**Whole paths for ours (round 8, p00 critic).** When ours has several expert paths, each is judged
whole, every metric from that one path; ours wins when one of its paths wins on its own. The
comparison shows that path (`ours_path`; when none wins, the path that wins the most metrics) and
lists every path's verdict (`ours_paths`). Ours' result counts are likewise one path's own
(`counts_path`). The reference stays at its best path on each metric (below), so beating it is
beating every one of its paths whole. Over repeats (`--repeat N`) each path keeps the median of its
own times (`median_counts`).

A scroll (`op.scrollTo`) is a step modelled like a click (P + BB), so a path that needs one never
looks free. A click that makes the product send a file (`op.clickForDownload`) is one step; the
wait for the file is system wait.

**Expert paths per metric.** Where the shortest path depends on the metric (hotkeys press more
keys but save pointing and hand moves), a driver offers `variants` (for example `keyboard` and
`pointer`). Each runs in full; the reference's result counts, per metric, the best verified variant
(`best_path_per_metric`) and records every variant's steps; `system_wait_seconds` is the wait inside the variant whose clock is counted. The reference is never measured on a
path worse than the best one an expert could take for that metric. Ours is judged on whole paths
(above). A variant may define its own
`setup`, `signIn`, `ready`, `verify` and `cleanup`; each overrides the base driver's (round 7: they
ran only when the base driver defined the same hook).

**Baselines stay honest.** Each result records a hash of the driver that produced it.
`test/baselines.test.mjs` (part of `./erp verify`, no rig needed) fails when a driver changed
after its baseline, or when a baseline's counts do not follow from its recorded steps; re-run
`node run.mjs --task <id> --product odoo --repeat 3`. `npm run test:live` re-drives every task on
the rig.

## Blind screenshots

Each run takes screenshots at its key moments (`start`, the task's declared `moments`, `done`);
the blind page captions them `start`, `moment 1`, `moment 2` … `done`, never with the driver's own
moment names. Placeholders that name the vendor are emptied before the shot rather than painted
over, so a filled-in field is never singled out. Only the `blind/` folder (its `shots/` and
`review.html`) is blind; hand a reviewer that folder alone. `results/`, `comparisons/` and
`key.json` sit outside it and name the products by design. Every screenshot carries the same file
time (2000-01-01), and the products run in a random order per task, so neither file times nor run
order tell the products apart.
Logos, product names, vendor links and the vendor's bot avatar are painted over with a flat grey
box, and so are the demo data's own names (company names, database or tenant codes, company and
branch codes, and, from round 8, the names of the people each product signs in as, the task
fixtures' company codes and Arabic names; `identity` in `lib/blind.mjs`). Round 7: every product's shots mask every product's
names, not only their own: a name masked in one product's shots and showing in the other's told the
products apart. A name inside a cell that hides its overflow (a list cell with an ellipsis) is
painted over by the whole cell, so the paint lines up with the columns (`maskTargets`). Round 9
(critic p01 r8): a name inside a form field is a value, not text, and was left showing ("Al Noor
Trading LLC" and ALN-DXB in our company form); every field whose value holds such a name or code
is painted over too, by the same rules (`revealsIdentity`), and every field when the values cannot
be read. A task one product cannot run yet shows no columns on the review page (a "not built"
column named its product). The shot is rendered in greyscale (no signature colours); the
title and favicon are replaced. File names are random hex; `key.json` (outside `blind/`) maps
them back. `--product both` also writes `review.html`: the two products as A and B, assigned
at random per task, mapping in `key.json`. Our product marks any branding element with
`data-brand` (painted over too); set `COMPARE_OURS_BRAND_WORDS=Name1,Name2` once it has a name.
When a driver has several expert paths, the shots come from the path that is best on the most
metrics (`screenshots_path` in the result), so a reviewer sees the path the counts mostly describe.

## Output

```
<out>/blind/shots/<random>.jpg  blind screenshots              } the only part a blind reviewer sees
<out>/blind/review.html         blind side-by-side page (--product both)  }
<out>/results/<run-id>.json     one JSON per run: counts, every step with timing, waits, screenshots, verification
<out>/key.json                  screenshot -> product, task, moment; A/B letters and run order per task
<out>/comparisons/<task>.json   verdict and per-measure outcome (--product both)
<out>/failures/failure-<random>.jpg  page at an error before the measured part (not in blind/)
```

A run that ends in an error records `failure_capture` in its result: the page's address, its last
40 console lines and page errors and, for an error before the measured part (no operator, so no
`error` shot), a screenshot in `<out>/failures/` (never in the reference folder). A failed
`./erp verify` keeps its whole output folder, the health check's results included, in
`.verify-failed/<time>-<pid>/` (or `ERP_VERIFY_KEEP_DIR`) instead of deleting it.

Baselines (`--product odoo` without `--out`) are kept one per task in
`gauntlet/reference/odoo/tasks/<task>.json` with their shots in `gauntlet/reference/odoo/shots/`
(every shot there is the reference's own, so that folder has no `blind/` part).
`gauntlet/reference/odoo/volume.json` records the verified reference volume.

## Tasks

Task definitions (`tasks/<id>.mjs`) are product-neutral: actor, start, goal, done. Each task has
one driver per product: `drivers/odoo/<id>.mjs` and `drivers/ours/<id>.mjs`. Tasks are never
removed (plan.md); the ratchet counts them.

| Task | Odoo path (shortest expert path found) | Notes |
|---|---|---|
| find-record | Apps > Contacts (or Ctrl+K > "/contacts" > Enter) > type the name > Enter > open the result | 100,000 contacts from the shared dataset; menus and palette variants |
| create-restricted-user | Apps > Settings > Manage Users (or Ctrl+K > "/users") > New (Alt+C) > name > Tab/click > login > Contact: Creation > Save (Alt+S) | keyboard, pointer and palette variants; other privileges default to No |
| custom-field-filter | find the contact > ⋮ > Edit Properties > label > Add > value > Save > Contacts > Backspace > value > Search Properties > Licence ref | Studio (real fields) is Enterprise; Community's no-code custom field is a property |
| switch-to-arabic | user menu > My Preferences > Language: Arabic > Update Preferences > F5 | the tester works in Contacts (a real working screen); Odoo keeps the left-to-right layout until the page is reloaded |
| import-5000 | Apps > Contacts > ⋮ > Import > Upload (file) > Import | headers map automatically |
| follow-approval | Apps > Purchase > open the order waiting for approval (first row) > Approve Order | Approvals is Enterprise; nearest Community feature is purchase two-step approval (limit AED 5,000) |
| sign-in (p00) | type the e-mail (focused) > Tab > password > Enter | same user, e-mail and password created in both products; `new-device` and `returning` (the browser signed in and out before; whatever a product remembers is used) variants in both |
| find-user (p03) | Ctrl+K > "/users" > Enter (or Apps > Settings > Manage Users) > type the name, or the shortest piece of it that puts the user on the list's first screen > Enter (or click the search box's first suggestion) > open the user | the dataset's 100,000 users (ours: start with `ERP_SEED_USERS_CSV`); menus, palette and menus-suggestion variants (the suggestion click saves the Enter key, found by the p05 round 4 critic), and since round 9 the shortest-piece variants (`shortest-suggestion`, `shortest-enter`, `palette-shortest`: the p05 round 7 critic's "il pi" and the suggestion, 5 keystrokes; the piece is found through the back end before the run, as ours finds its shortest prefixes) |
| api-update-user (p15) | POST /json/2/res.users/search > POST /json/2/res.users/write | through the API only: steps are requests, keystrokes the requests as typed; Odoo's JSON-2 needs an API key (an interactive identity check), so the same calls travel by its external JSON-RPC and are counted in the JSON-2 form |
| create-company-branch (p02) | Settings > Users & Companies > Companies > New > name > Branches > Add a line > branch > Save & Close > Save | keyboard and pointer variants |
| switch-company (p02) | company switcher > the company | |
| keyboard-navigation (p04) | Alt+H > Down, Down > Enter > Down ×4 > Enter | no mouse allowed (`keyboardOnly`, judged by the harness); the form's pager shows it is the list's third record |
| edit-and-save (p06) | phone field > Ctrl+A > type > Save | keyboard and pointer variants |
| arabic-report (p06) | user menu > My Preferences > Arabic > Update Preferences > Print | printing straight away gives Arabic text laid out left to right |
| who-changed-field (p07) | Apps > Contacts > name > Enter > open; the change log shows the latest change | change made by another user in set-up |
| add-rate (p08) | Invoicing > Configuration > Currencies > EUR > Add a line > AED per unit > Save | keyboard and pointer variants |
| configure-sequence (p10) | Settings > scroll > developer mode > Technical > Sequences > search > open > prefix, size > Save | Odoo shows sequences only in developer mode |
| attach-file (p11) | paperclip > Attach files > choose the file | |
| see-and-rerun-job (p12) | Settings > scroll > developer mode > Technical > Scheduled Actions > search > open > Run Manually | Odoo shows no last-run time and no message after the run |
| export-filtered-list (p14) | Contacts > tag > Search Tag for > select page > Select all > Actions > Export > Export | the verification reads the workbook |
| print-list-arabic (p06) | (working in Arabic) Purchase orders list > type the vendor > Enter > header check box > Print > Purchase Order | the list report named in p06's spec; Community has no printed table of a list, its nearest feature prints the selected orders' own document in one PDF; the vendor's partners get Arabic in set-up (restored in clean-up); our product's stand-in list is the users list until purchase orders exist |

Every task names the piece (`piece`) whose critic fills in its `ours` driver. Built `ours` drivers:
sign-in, find-user, create-restricted-user, api-update-user, switch-to-arabic and
reach-screen-keyboard (p04), create-company-branch and switch-company (p02), edit-and-save and
arabic-report (p06; until contacts and purchase orders exist they use a company and its printed
company profile as the stand-in record and document), print-list-arabic (the users list, printed as a
PDF report in Arabic, stands in for the vendor's purchase orders). Drivers find things by role and label, not layout.
The others report "not built yet" with what they wait for.

An API task (`channel: 'api'` in its definition) has no screens: the driver's `signIn` calls
`ctx.useApi({ baseUrl, headers, transport? })` (`transport` names a harness transport) outside the measurement and `run` sends each request
with `op.request(method, path, body)`. Its start and done screenshots show a neutral HTTP-client
transcript of the requests, rendered the same way for both products.

## Writing an `ours` driver

Replace the stub in `drivers/ours/<task>.mjs` (it reports `not_built` until then):

```js
export default {
  built: true,
  path: 'one line: the shortest expert path through our screens',
  async setup(ctx) {},          // fixtures through our API (not measured); never page script
  async signIn(ctx) {},         // sign in as the task's actor; for startAt 'record' or 'list', open that screen
  ready: 'css selector',        // optional: what the start screen shows once loaded (the runner waits for it)
  observe(ctx) {},              // optional: passive listeners on the start page (ctx.page.on('request', ...))
  async run(op, ctx) {          // measured: only op.click / op.type / op.fill / op.press / op.pickFile / op.waitFor / op.shot / op.request;
                                // ctx.page and op.page may only locate and read here (lib/guard.mjs);
                                // op.shot(moment) for each of the task's declared moments, once
    return {};
  },
  async verify(ctx) {           // read-only confirmation: { verified, details }; must fail before the clock.
                                // ctx alone, in a fresh process (round 9): set-up's ctx.state as data,
                                // ctx.downloads for files the measured part downloaded, no clock;
                                // a task that saves reads its end state from the back end
    return { verified: true, details: {} };
  },
  async cleanup(ctx) {},        // undo the task so it can run again
};
```

Drivers run in the driver process (see "What is measured"): `fetch` there goes through the harness
(the product's own address only), Node-side predicates cannot be passed to Playwright methods (use
strings, patterns or `ctx.until`), and files may be written only under `os.tmpdir()`.

`ctx` carries `page`, `context`, `browser`, `product` (base URL, demo sign-ins), `task` (its
`input`), `needles` (the dataset's records: `ctx.needles.contact.name` …), `dataDir` (the
generated files), `state` (shared by set-up, sign-in, run and clean-up in the run's process;
`verify()` gets it as data, as it stood when the start was ready), `downloads` (in `verify()`), `read(fn, arg)` and `until(fn, { arg })`
(page functions: read-only, see "Page functions") and `health` (true in a driver health check,
below). A page function reads with `document.querySelector…`, `innerText`, `getComputedStyle`,
`location.pathname`; index a list with a number or `.item(i)`; pass what it needs as `arg`. Use
keyboard-first paths where our product offers them: every key is counted, and so is every click.

**Health check.** `./erp verify` runs every built ours driver against its clean stack:
`node run.mjs --task built --product ours --health --out <dir>`. The clean stack has no comparison
dataset, so with `ctx.health` a driver's set-up creates the one dataset record its task needs.
A built driver that no longer verifies fails `./erp verify` (round 3: a list change broke
switch-to-arabic and nothing noticed). Its counts are not a comparison.

## Planting a fault (for critics)

`npm run mutations` (`scripts/mutations.mjs`) removes or weakens each defence of the instrument in
turn, in a scratch copy of the harness, and runs the self-tests that must catch it: the keystroke
operator, greyscale, the read world's click refusal and its arming, the source check, the driver
lint's page-function check, the network locks, the script freeze, the abort at the clock, the
screen check after `verify()`, the document settle, a variant's own hooks, the masks, the zero
rule, ties and whole paths, and (round 9) the verify() process (its clocks, reads, writes, the Date
header), the no-step refusal, the saved-state rule and its pass gap, the entered-value checks, the
keyboard-only rule, chord keystrokes, scroll steps, the reference's best path, the refusal of
requests after the clock, the verify read limit, field-value masks and the review page's unbuilt
tasks. Each test file's self-tests run once unmutated for all of its
mutations (the control), and a mutation counts as caught only when a test that passed there fails
mutated. It exits 1 when a mutation is missed. `./erp verify` runs it after the unit tests (a missed mutation fails the
web stage), and `test/ratchet.test.mjs` keeps the number of mutations at or above
`compare.instrumentMutations` and checks that each still finds the text it mutates. Add a line
there for every new defence.

Plant tests run drivers the way the runner does: as module files in the driver process. Write the
driver as a module (or transform a real one) and hand its description to `execute()`:

```js
import { execute, layout } from '../lib/runner.mjs';
import { describeDriverFile } from '../lib/registry.mjs';
const r = await execute(task, await describeDriverFile('/tmp/plant.mjs'), product, 'ours', needles, layout('/tmp/out'), { timeout: 10_000 });
```

`test/helpers/driver-module.mjs` writes an inline driver object as such a module (`sandboxed`), a
module from source text (`sandboxedSource`), or a planted copy of a real driver (`plantedFile`).
A driver object handed to `execute()` directly is refused (`invalid`): it would run in the harness.

## Shared dataset

`data/generate.mjs` writes the same 100,000 contacts, 100,000 users, 100,000 exchange rates and
the 5,000-row import file for both products, deterministically (seeded PRNG; byte-for-byte
reproducible, checked by the tests). The files are not committed; any harness command
regenerates them when missing. `needles.json` names the record "find one among 100,000" looks for
(its name occurs exactly once).

Loading it into our product: `ERP_SEED_USERS_CSV=gauntlet/compare/data/out/users.csv ./erp up`
(on a fresh database: `./erp down --volumes` first) makes the demo tenant's bulk users exactly the
dataset's users, with the names, sign-ins, languages and active flags the Odoo rig holds
(`users.csv` has an `active` column; every dataset user is active in both products). Contacts and rates
follow the same pattern when their pieces exist (p16, p08).
