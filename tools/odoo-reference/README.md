# Odoo Community reference rig

The reference product for the blind comparison (`gauntlet/compare/`). Odoo is a reference only
(CLAUDE.md rule 7): never copy its code, views or text into the product.

```bash
tools/odoo-reference/up.sh            # start (or top up) the shared rig: http://localhost:8069
tools/odoo-reference/up.sh --status   # what is running and the recorded volume
tools/odoo-reference/down.sh          # stop it (data kept); --purge also deletes its volumes
```

`up.sh` is idempotent: it starts PostgreSQL and Odoo (`odoo:20.0-20260926`, pinned) under the
compose project `b-p01-odoo-rig`, creates the `reference` database with Contacts, Discuss,
Purchase and base import, activates Arabic, loads at least 100,000 rows into every main list,
refreshes planner statistics, waits for the web client and writes the verified counts to
`gauntlet/reference/odoo/volume.json`. A second run adds only what is missing (a few seconds).
A first run on an empty machine takes a few minutes.

Sign-ins (local rig only, bound to 127.0.0.1): `admin`/`admin`, `approver`/`approver` (purchase
manager), `buyer`/`buyer` (purchase user). The harness adds `lang.tester` for the language task.

| Main list | Odoo model | Our counterpart |
|---|---|---|
| contacts | res.partner (shared dataset) | Contacts (p16) |
| users | res.users (shared dataset) | Users (p03) |
| currency_rates | res.currency.rate (shared dataset) | Exchange rates (p08) |
| audit_messages | mail.message, field-change tracking | Audit trail (p07) |
| attachments | ir.attachment on contacts | Attachments (p11) |
| job_runs | ir.cron.progress | Background job runs (p12) |
| approvals | purchase.order (200 waiting for approval) | Approval flows (p13) |

Odoo deletes scheduled-job run records older than a week; run `up.sh` again before a comparison
that uses job runs and it tops them back up.

Environment overrides: `ODOO_REF_PROJECT`, `ODOO_REF_PORT` (8069), `ODOO_REF_DB` (reference),
`ODOO_REF_TARGET` (100000), `ODOO_REF_IMAGE`, `ODOO_REF_VOLUME_OUT`.
