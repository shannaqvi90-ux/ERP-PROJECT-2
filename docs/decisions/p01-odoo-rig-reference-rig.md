# p01 — Odoo reference rig: pinned Community image, SQL-cloned volume, one stable project

Date: 2026-10-02. Piece: p01-odoo-rig. Status: accepted.

## Decision

- **Image.** Odoo Community `odoo:20.0-20260926` (pinned dated tag of the official image) with its
  own `postgres:17-alpine`. Odoo runs threaded (`--workers=0`) with one database and no database
  manager, bound to 127.0.0.1:8069.
- **One stable compose project, `b-p01-odoo-rig`.** Every critic's `up.sh` reuses the same
  containers and volumes instead of building a second 100,000-row Odoo. The server container
  mounts only named volumes, so it never depends on the checkout that started it; the seed runs in
  a one-off container that mounts the seed script and the shared dataset.
- **Volume by cloning.** Small configuration (companies, branches, users for the tasks, the
  purchase two-step approval at AED 5,000, tags, product) goes through Odoo's ORM. Each 100,000-row
  list is loaded with set-based SQL that clones one ORM-created template row, so every column holds
  what Odoo itself would write, and loading takes minutes instead of hours. Contacts, users and
  rates come from the shared dataset (`gauntlet/compare/data/`) that our product also loads.
- **Main lists** and their closest Odoo counterparts: contacts (res.partner), users (res.users),
  exchange rates (res.currency.rate), audit (mail.message field tracking), attachments
  (ir.attachment), job runs (ir.cron.progress), approvals (purchase.order). `up.sh` verifies the
  counts and writes them to `gauntlet/reference/odoo/volume.json`; it fails if a list is short.
- **Planner statistics.** `ANALYZE` after seeding, committed. Without it PostgreSQL plans the bulk
  tables as empty and Odoo's purchase dashboard took 150 s instead of 0.2 s, which would have
  handicapped the reference.
- **Realistic history.** Bulk purchase orders belong to the buyer user and confirmed ones are
  received and billed, as in a working company; 1 in 500 waits for approval.

## Why

- A tie is a loss and Odoo is the bar: the reference must be fast and realistic, never slowed by
  our rig. Odd data (orders owned by the system bot, missing statistics) was fixed when found.
- Cloning template rows avoids hand-writing Odoo's column semantics (and copying any of its code)
  while staying fast enough to rebuild the rig on a clean machine.
- A pinned image keeps baselines comparable over the run; a later Odoo release is a new baseline,
  not a silent change.

## Consequences

- Odoo vacuums ir.cron.progress rows older than a week; `up.sh` tops them up, so run it before a
  comparison that involves job runs.
- The project name carries the builder prefix (`b-p01-…`) because that is where the rig was first
  seeded; renaming it means reseeding or copying volumes (a lead decision, not a builder's).
