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

Options: `--task <id|id,id|all>`, `--product odoo|ours|both`, `--out <dir>`, `--repeat N`
(median machine seconds of N runs), `--headed`. Exit code 1 when a run fails, errors or is invalid.
With `--product both` the two products run in a random order per task (recorded in `key.json`).

Tests: `npm test` (unit tests; the live rig checks run too when the rig answers),
`npm run test:live` (also drives every Odoo task on the rig, and fails if the rig is down).
`./erp verify` runs the unit tests and counts them against `suite.compareTests` in
`gauntlet/ratchet.json`.

The Odoo reference must be running: `tools/odoo-reference/up.sh` (see its README).

## What is measured

Every driver acts only through the instrumented operator (`lib/operator.mjs`), so both products
are counted the same way. This is enforced, not trusted (`lib/sandbox/`, `lib/guard.mjs`,
plant-tested in `test/guard.test.mjs` and `test/sandbox.test.mjs`, linted in
`test/drivers-lint.test.mjs`):

- **Drivers never run in the harness process** (round 5). Every harness process starts one driver
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
  cannot close the sentinel's call and run page script outside it.
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
  both run `fn` inside the sentinel below.
- The condition of `op.waitFor(fn)` (and of `ctx.read`, `ctx.until`) runs in the page inside a
  sentinel that refuses clicks, focus, value and scroll setters, form submits, timers, network,
  storage and history calls, cancels a navigation, and reports any DOM change, event, navigation
  or focus move it caused.
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

The clock (`machine_seconds`) starts at the first measured action and stops when `run` returns,
right after its last step or wait. Round 5 closes the ways to finish a task after that:

- If a request that changes the product (a document load, or any method but GET, HEAD and
  OPTIONS that is not one of the reference's documented read calls) is still under way when `run`
  returns, the clock runs on until it ends: a save is the product's answer to the task (system
  wait "the product still answering when run() returned"). Reads still loading (avatars, a chatter)
  are not waited for.
- When the clock stops the page's own script is frozen (no timer, network callback or animation
  frame of the product runs any more; reading and screenshots still work) and its new requests are
  aborted (`requests_after_clock`), so the screen `verify()` reads is the screen at the end of the
  measured part. It is thawed for clean-up.
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
Results record the instrument version (`INSTRUMENT_VERSION` in `lib/runner.mjs`, now 5); a baseline
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

A scroll (`op.scrollTo`) is a step modelled like a click (P + BB), so a path that needs one never
looks free. A click that makes the product send a file (`op.clickForDownload`) is one step; the
wait for the file is system wait.

**Expert paths per metric.** Where the shortest path depends on the metric (hotkeys press more
keys but save pointing and hand moves), a driver offers `variants` (for example `keyboard` and
`pointer`). Each runs in full; the result counts, per metric, the best verified variant
(`best_path_per_metric`) and records every variant's steps; `system_wait_seconds` is the wait inside the variant whose clock is counted. The reference is never measured on a
path worse than the best one an expert could take for that metric.

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
box, and so are the demo data's own names (each product's company name and its database or tenant
code, `identity` in `lib/blind.mjs`); the shot is rendered in greyscale (no signature colours); the
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
```

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
| find-user (p03) | Ctrl+K > "/users" > Enter (or Apps > Settings > Manage Users) > type the name > Enter > open the result | the dataset's 100,000 users (ours: start with `ERP_SEED_USERS_CSV`); menus and palette variants |
| api-update-user (p15) | POST /json/2/res.users/search > POST /json/2/res.users/write | through the API only: steps are requests, keystrokes the requests as typed; Odoo's JSON-2 needs an API key (an interactive identity check), so the same calls travel by its external JSON-RPC and are counted in the JSON-2 form |
| create-company-branch (p02) | Settings > Users & Companies > Companies > New > name > Branches > Add a line > branch > Save & Close > Save | keyboard and pointer variants |
| switch-company (p02) | company switcher > the company | |
| keyboard-navigation (p04) | Alt+H > Down, Down > Enter > Down ×4 > Enter | no mouse allowed; verified |
| edit-and-save (p06) | phone field > Ctrl+A > type > Save | keyboard and pointer variants |
| arabic-report (p06) | user menu > My Preferences > Arabic > Update Preferences > Print | printing straight away gives Arabic text laid out left to right |
| who-changed-field (p07) | Apps > Contacts > name > Enter > open; the change log shows the latest change | change made by another user in set-up |
| add-rate (p08) | Invoicing > Configuration > Currencies > EUR > Add a line > AED per unit > Save | keyboard and pointer variants |
| configure-sequence (p10) | Settings > scroll > developer mode > Technical > Sequences > search > open > prefix, size > Save | Odoo shows sequences only in developer mode |
| attach-file (p11) | paperclip > Attach files > choose the file | |
| see-and-rerun-job (p12) | Settings > scroll > developer mode > Technical > Scheduled Actions > search > open > Run Manually | Odoo shows no last-run time and no message after the run |
| export-filtered-list (p14) | Contacts > tag > Search Tag for > select page > Select all > Actions > Export > Export | the verification reads the workbook |

Every task names the piece (`piece`) whose critic fills in its `ours` driver. Built `ours` drivers:
sign-in, find-user, create-restricted-user, api-update-user, switch-to-arabic and
reach-screen-keyboard (p04), create-company-branch and switch-company (p02), edit-and-save and
arabic-report (p06; until contacts and purchase orders exist they use a company and its printed
company profile as the stand-in record and document). Drivers find things by role and label, not layout.
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
  async verify(ctx, outcome) {  // read-only confirmation: { verified, details }; must fail before the clock
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
generated files), `state` (shared between the hooks), `read(fn, arg)` and `until(fn, { arg })`
(page script inside the sentinel) and `health` (true in a driver health check, below). Use
keyboard-first paths where our product offers them: every key is counted, and so is every click.

**Health check.** `./erp verify` runs every built ours driver against its clean stack:
`node run.mjs --task built --product ours --health --out <dir>`. The clean stack has no comparison
dataset, so with `ctx.health` a driver's set-up creates the one dataset record its task needs.
A built driver that no longer verifies fails `./erp verify` (round 3: a list change broke
switch-to-arabic and nothing noticed). Its counts are not a comparison.

## Planting a fault (for critics)

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
