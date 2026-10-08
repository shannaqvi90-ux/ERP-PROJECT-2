# p01-odoo-rig: keeping the comparison instrument fair and current

Date: 2026-10-03. Context: critic p01 round 1 found the reference measured on a path that is not
the shortest for every metric, a language task measured on an empty screen, driver-specific
captions on the blind page, the six baselines never re-checked by the one command, no task for
most pieces, and no way to load the shared dataset into our product.

## Decisions

1. **Expert-path variants, best per metric.** The shortest path differs by metric: hotkeys
   (Alt+C, Alt+S, Tab) press more keys but save pointing and hand moves. A driver may offer
   `variants`; the runner runs each in full and counts, per metric, the best verified one
   (`best_path_per_metric`). Comparing our product against the per-metric best is stricter than
   against any single path (a tie is still a loss).
2. **Scrolls and downloads are counted.** `op.scrollTo` is a step modelled like a click (the
   keystroke-level model has no scroll operator; treating it as free would favour a product
   whose controls sit below the fold). `op.clickForDownload` is one step; the wait for the file
   is system wait.
3. **Baselines carry the driver's hash.** `test/baselines.test.mjs` runs in `./erp verify`
   without the rig: a baseline produced by an older driver, or whose counts do not follow from
   its recorded steps, fails. Live re-driving stays in `npm run test:live` (it needs the rig,
   which the one command does not start). `compare.harnessTests` now counts only tests that run
   in `./erp verify`; `compare.liveTests` counts the live checks.
4. **A task for every piece.** Eleven tasks added (sign-in, company and branch, switch company,
   keyboard navigation, edit and save, Arabic report, who changed a field, add a rate, configure
   a sequence, attach a file, see and rerun a job, export a filtered list), each with a verified
   Odoo driver and an `ours` stub naming the piece whose critic builds it. Where Odoo cannot meet
   part of a goal (no last-run time for jobs; Arabic text laid out left to right unless the user
   switches language) the driver takes Odoo's nearest path and the task notes say so.
5. **Same users in both products.** `ERP_SEED_USERS_CSV=<users.csv> ./erp up` seeds the demo
   tenant's bulk users from the shared dataset (Erp:Seed:UsersCsv), as the Odoo rig does. The
   verify stack keeps generated users. Contacts and rates follow when p16 and p08 exist.
6. **Rig realism.** Change log entries show the contact's real e-mail and a real author; every
   bulk attachment holds its own document in the database. Both are repairs the idempotent seed
   applies to existing rigs.

## Not done here

- An API task (p15) needs a different instrument (requests and payloads rather than keys);
  left to p15's critic.
- Moving the reference captures to `bar/reference/` waits for the owner (human gate).
