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
| steps | each click, each key chord, each field entry (typing a value), each file pick |
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

## Blind screenshots

Each run takes screenshots at its key moments (`start`, named moments inside the driver, `done`).
Logos, product names, vendor links, the vendor's bot avatar and placeholders naming the vendor are
painted over with a flat grey box; the shot is rendered in greyscale (no signature colours); the
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
| create-restricted-user | Apps > Settings > Manage Users > New > name > Login > Contact: Creation > Save | other privileges default to No |
| custom-field-filter | find the contact > ⋮ > Edit Properties > label > Add > value > Save > Contacts > Backspace > value > Search Properties > Licence ref | Studio (real fields) is Enterprise; Community's no-code custom field is a property |
| switch-to-arabic | user menu > My Preferences > Language: Arabic > Update Preferences > F5 | Odoo shows Arabic labels at once but keeps the left-to-right layout until the page is reloaded |
| import-5000 | Apps > Contacts > ⋮ > Import > Upload (file) > Import | headers map automatically |
| follow-approval | Apps > Purchase > open the order waiting for approval (first row) > Approve Order | Approvals is Enterprise; nearest Community feature is purchase two-step approval (limit AED 5,000) |

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
