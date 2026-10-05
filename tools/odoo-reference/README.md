# Odoo Community reference rig

The reference product for the blind comparison (`gauntlet/compare/`). Odoo is a reference only
(CLAUDE.md rule 7): never copy its code, views or text into the product.

```bash
tools/odoo-reference/up.sh            # start (or top up) the shared rig: http://localhost:8069
tools/odoo-reference/up.sh --status   # what is running and the recorded volume
tools/odoo-reference/down.sh          # stop it (data kept); --purge also deletes its volumes
```

`up.sh` is idempotent: it starts PostgreSQL and Odoo (`odoo:20.0-20260926`, pinned) under the
compose project `odoo-reference`, creates the `reference` database with Contacts, Discuss,
Purchase and base import, activates Arabic, loads at least 100,000 rows into every main list,
refreshes planner statistics, waits for the web client and writes the verified counts to
`gauntlet/reference/odoo/volume.json`. A second run adds only what is missing (a few seconds).
A first run on an empty machine takes about three minutes once the images are pulled on an idle machine (measured: 2 min 34 s), and up to about nine minutes on a machine shared with other builds (measured by the round 3 critic: 506 s). A second run adds nothing and takes about half a minute.

Sign-ins (local rig only, bound to 127.0.0.1): `admin`/`admin`, `approver`/`approver` (purchase
manager), `buyer`/`buyer` (purchase user). The harness adds task users as it needs them:
`lang.tester` (switch to Arabic; works in Contacts), `noor.editor` (makes the change the
who-changed-field task looks for) and `signin.tester@demo-trading.example` (sign in).

| Main list | Odoo model | Our counterpart |
|---|---|---|
| contacts | res.partner (shared dataset) | Contacts (p16) |
| users | res.users (shared dataset) | Users (p03) |
| currency_rates | res.currency.rate (shared dataset) | Exchange rates (p08) |
| audit_messages | mail.message, field-change tracking | Audit trail (p07) |
| attachments | ir.attachment on contacts | Attachments (p11) |
| job_runs | ir.cron.progress | Background job runs (p12) |
| approvals | purchase.order (200 waiting for approval) | Approval flows (p13) |

Realism of the bulk rows: every change log entry names one of the company's users and shows the
contact's actual e-mail as the new value; every attachment holds its own small document (kept in
the database), so no two of the 100,000 share content. Earlier rigs are repaired on the next run.

Odoo deletes scheduled-job run records older than a week; run `up.sh` again before a comparison
that uses job runs and it tops them back up.

**Never stop the shared rig** (project `odoo-reference`): every critic uses it. Its data sits in
external volumes (`odoo-reference-db`, `odoo-reference-filestore`, see `compose.shared.yaml`) that
no `docker compose down -v` removes; `down.sh --purge` on it also needs
`ODOO_REF_CONFIRM_PURGE=odoo-reference`. A private copy for a clean-clone check uses its own
project and port (`ODOO_REF_PROJECT=c-p01-odoo-rig-r3-odoo ODOO_REF_PORT=20152 tools/odoo-reference/up.sh`)
and has ordinary volumes that its own `down.sh --purge` deletes. A rig started under the old
project name `b-p01-odoo-rig` is adopted in place by the next `up.sh` (data moved, not reseeded).

Environment overrides: `ODOO_REF_PROJECT`, `ODOO_REF_PORT` (8069), `ODOO_REF_DB` (reference),
`ODOO_REF_TARGET` (100000), `ODOO_REF_IMAGE`, `ODOO_REF_VOLUME_OUT`.
