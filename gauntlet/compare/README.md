# Blind comparison harness

Measures our product and the Odoo Community reference on the same tasks, the same data and the
same instrument, so critics can judge "better than Odoo" from numbers and blind screenshots
instead of impressions. Odoo is a reference only (CLAUDE.md rule 7): nothing here copies its
code, views or text; drivers operate its screens as a user would.

## One command

```bash
cd gauntlet/compare && npm ci                    # once (playwright-core only; uses the preinstalled Chromium)

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
are counted the same way. This is enforced, not trusted (`lib/guard.mjs`, plant-tested in
`test/guard.test.mjs`, linted in `test/drivers-lint.test.mjs`):

- `run(op, ctx)` gets a view of the operator with the counted actions only: no clock, no
  `start`/`finish`, read-only copies of the steps.
- `ctx.page`, `ctx.context`, `ctx.browser` and `op.page` are guarded proxies, and so is everything
  reached through them (locators, keyboard, mouse, frames, `page.request`). While a task is
  measured they allow only locating and reading (`locator`, `getByRole`, `count`, `inputValue`,
  `textContent`, `boundingBox`, `url` …). A click, fill, key press, mouse action, `goto`, `reload`,
  `evaluate`, new page or new context throws, and the run is recorded as **invalid** (never
  verified), even if the driver catches the error.
- The condition of `op.waitFor(fn)` runs in the page inside a sentinel that refuses clicks, focus,
  value and scroll setters, form submits, timers, network, storage and history calls, and reports
  any DOM change, event, navigation or focus move it caused.
- While measured, `fetch` and `http(s).request` from the harness are refused, so a task cannot be
  done through the back end and count nothing. API tasks use `op.request`, which counts.
- Drivers may import only `./_common.mjs`, `lib/ours-api.mjs`, `lib/odoo-rpc.mjs`, `lib/xlsx.mjs`
  and Node's file helpers; no Playwright, no network module, no `eval` or dynamic import.

Set-up, sign-in, verification and clean-up run outside the measurement and may use the page freely.

The clock (`machine_seconds`) starts at the first measured action and stops when `run` returns,
right after its last step or wait. Screenshots taken while it runs are taken out of it; the `done`
screenshot is taken after it stops. `test/baselines.test.mjs` checks every baseline: machine seconds
end within 0.5 s after the last step or wait and never before it, and the waits never exceed the
clock. Results record the instrument version (`INSTRUMENT_VERSION` in `lib/runner.mjs`, now 3); a
baseline from an older instrument fails the check until it is re-captured.

| Measure | Definition |
|---|---|

| Measure | Definition |
|---|---|
| steps | each click, each key chord, each field entry (typing a value), each file pick, each scroll; in an API task each HTTP request |
| keystrokes | each key pressed; Shift counts; a chord counts each of its keys; an API request counts as typed (method, path and query, compact JSON body) plus Enter |
| machine_seconds | wall clock from the first step to the verified end state on screen; screenshot time excluded |
| system_wait_seconds | the part of machine seconds spent waiting for the product to respond |
| human_seconds | keystroke-level model of the steps (operators below); system response not included |
| human_plus_wait_seconds | human_seconds + system_wait_seconds |

Keystroke-level model operator times (Card, Moran & Newell, CACM 23(7), 1980): K 0.28 s (average
non-secretary typist), P 1.10 s, B 0.10 s per press or release (a click is 0.20 s), H 0.40 s,
M 1.35 s. One M before every step except a step marked as a continuation (Enter right after typing,
typing into the field the previous click focused); one H whenever the hand moves between mouse and
keyboard. Details and the exact rules: `lib/klm.mjs`.

Start state, for both products: signed in (outside the measurement, through the product's
session), on the screen the product shows right after sign-in. End state: the task's "done" on
screen, then confirmed through the product's back end (not timed).

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

Each run takes screenshots at its key moments (`start`, named moments inside the driver, `done`);
the blind page captions them `start`, `moment 1`, `moment 2` … `done`, never with the driver's own
moment names. Placeholders that name the vendor are emptied before the shot rather than painted
over, so a filled-in field is never singled out. Only the `blind/` folder (its `shots/` and
`review.html`) is blind; hand a reviewer that folder alone. `results/`, `comparisons/` and
`key.json` sit outside it and name the products by design. Every screenshot carries the same file
time (2000-01-01), and the products run in a random order per task, so neither file times nor run
order tell the products apart.
Logos, product names, vendor links and the vendor's bot avatar are painted over with a flat grey
box; the shot is rendered in greyscale (no signature colours); the
title and favicon are replaced. File names are random hex; `key.json` (outside `blind/`) maps
them back. `--product both` also writes `review.html`: the two products as A and B, assigned
at random per task, mapping in `key.json`. Our product marks any branding element with
`data-brand` (painted over too); set `COMPARE_OURS_BRAND_WORDS=Name1,Name2` once it has a name.

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
sign-in, find-user (opens the user's record; it fails until the users screen has a record view),
api-update-user, and switch-to-arabic (p04). Drivers find things by role and label, not layout.
The others report "not built yet" with what they wait for.

An API task (`channel: 'api'` in its definition) has no screens: the driver's `signIn` calls
`ctx.useApi({ baseUrl, headers, transport? })` outside the measurement and `run` sends each request
with `op.request(method, path, body)`. Its start and done screenshots show a neutral HTTP-client
transcript of the requests, rendered the same way for both products.

## Writing an `ours` driver

Replace the stub in `drivers/ours/<task>.mjs` (it reports `not_built` until then):

```js
export default {
  built: true,
  path: 'one line: the shortest expert path through our screens',
  async setup(ctx) {},          // fixtures through our API (not measured)
  async signIn(ctx) {},         // sign in as the task's actor; land on the post-sign-in screen
  async run(op, ctx) {          // measured: only op.click / op.type / op.fill / op.press / op.pickFile / op.waitFor / op.shot / op.request;
                                // ctx.page and op.page may only locate and read here (lib/guard.mjs)
    return {};
  },
  async verify(ctx, outcome) {  // back-end confirmation: { verified, details }
    return { verified: true, details: {} };
  },
  async cleanup(ctx) {},        // undo the task so it can run again
};
```

`ctx` carries `page`, `context`, `browser`, `product` (base URL, demo sign-ins), `task` (its
`input`), `needles` (the dataset's records: `ctx.needles.contact.name` …), `dataDir` (the
generated files) and `state` (shared between the hooks). Use keyboard-first paths where our
product offers them: every key is counted, and so is every click.

## Shared dataset

`data/generate.mjs` writes the same 100,000 contacts, 100,000 users, 100,000 exchange rates and
the 5,000-row import file for both products, deterministically (seeded PRNG; byte-for-byte
reproducible, checked by the tests). The files are not committed; any harness command
regenerates them when missing. `needles.json` names the record "find one among 100,000" looks for
(its name occurs exactly once).

Loading it into our product: `ERP_SEED_USERS_CSV=gauntlet/compare/data/out/users.csv ./erp up`
(on a fresh database: `./erp down --volumes` first) makes the demo tenant's bulk users exactly the
dataset's users, with the names, sign-ins and languages the Odoo rig holds. Contacts and rates
follow the same pattern when their pieces exist (p16, p08).
