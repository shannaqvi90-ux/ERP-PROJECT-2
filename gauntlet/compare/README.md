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
(median machine seconds of N runs), `--headed`. Exit code 1 when a run fails or errors.

Tests: `npm test` (unit tests; the live rig checks run too when the rig answers),
`npm run test:live` (also drives every Odoo task on the rig, and fails if the rig is down).
`./erp verify` runs the unit tests and counts them against `suite.compareTests` in
`gauntlet/ratchet.json`.

The Odoo reference must be running: `tools/odoo-reference/up.sh` (see its README).

## What is measured

Every driver acts only through the instrumented operator (`lib/operator.mjs`), so both products
are counted the same way:

| Measure | Definition |
|---|---|
| steps | each click, each key chord, each field entry (typing a value), each file pick, each scroll |
| keystrokes | each key pressed; Shift counts; a chord counts each of its keys |
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
(`best_path_per_metric`) and records every variant's steps. The reference is never measured on a
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
over, so a filled-in field is never singled out. Only `shots/` and `review.html` are blind:
`results/`, `comparisons/` and `key.json` name the products and their screens by design.
Logos, product names, vendor links and the vendor's bot avatar are painted over with a flat grey
box; the shot is rendered in greyscale (no signature colours); the
title and favicon are replaced. File names are random hex; `key.json` beside the `shots/` folder
maps them back. `--product both` also writes `review.html`: the two products as A and B, assigned
at random per task, mapping in `key.json`. Our product marks any branding element with
`data-brand` (painted over too); set `COMPARE_OURS_BRAND_WORDS=Name1,Name2` once it has a name.

## Output

```
<out>/results/<run-id>.json     one JSON per run: counts, every step with timing, waits, screenshots, verification
<out>/shots/<random>.jpg        blind screenshots
<out>/key.json                  screenshot -> product, task, moment; A/B letters per task
<out>/comparisons/<task>.json   verdict and per-measure outcome (--product both)
<out>/review.html               blind side-by-side page (--product both)
```

Baselines (`--product odoo` without `--out`) are kept one per task in
`gauntlet/reference/odoo/tasks/<task>.json` with their shots in `gauntlet/reference/odoo/shots/`.
`gauntlet/reference/odoo/volume.json` records the verified reference volume.

## Tasks

Task definitions (`tasks/<id>.mjs`) are product-neutral: actor, start, goal, done. Each task has
one driver per product: `drivers/odoo/<id>.mjs` and `drivers/ours/<id>.mjs`. Tasks are never
removed (plan.md); the ratchet counts them.

| Task | Odoo path (shortest expert path found) | Notes |
|---|---|---|
| find-record | Apps > Contacts > type the name > Enter > open the result | 100,000 contacts from the shared dataset |
| create-restricted-user | Apps > Settings > Manage Users > New (Alt+C) > name > Tab/click > login > Contact: Creation > Save (Alt+S) | keyboard and pointer variants; other privileges default to No |
| custom-field-filter | find the contact > ⋮ > Edit Properties > label > Add > value > Save > Contacts > Backspace > value > Search Properties > Licence ref | Studio (real fields) is Enterprise; Community's no-code custom field is a property |
| switch-to-arabic | user menu > My Preferences > Language: Arabic > Update Preferences > F5 | the tester works in Contacts (a real working screen); Odoo keeps the left-to-right layout until the page is reloaded |
| import-5000 | Apps > Contacts > ⋮ > Import > Upload (file) > Import | headers map automatically |
| follow-approval | Apps > Purchase > open the order waiting for approval (first row) > Approve Order | Approvals is Enterprise; nearest Community feature is purchase two-step approval (limit AED 5,000) |
| sign-in (p00) | type the e-mail (focused) > Tab > password > Enter | same user, e-mail and password created in both products |
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

Every task names the piece (`piece`) whose critic fills in its `ours` driver. The `ours` driver of
sign-in is built (p00's sign-in screen exists); the others report "not built yet".

## Writing an `ours` driver

Replace the stub in `drivers/ours/<task>.mjs` (it reports `not_built` until then):

```js
export default {
  built: true,
  path: 'one line: the shortest expert path through our screens',
  async setup(ctx) {},          // fixtures through our API (not measured)
  async signIn(ctx) {},         // sign in as the task's actor; land on the post-sign-in screen
  async run(op, ctx) {          // measured: only op.click / op.type / op.fill / op.press / op.pickFile / op.waitFor / op.shot
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
