# p01: the comparison harness checks the live rig's volume before every Odoo run

Date: 2026-10-07

## Context

The bar requires the Odoo reference to hold at least 100,000 records in each main list.
`tools/odoo-reference/up.sh` verifies that and writes `gauntlet/reference/odoo/volume.json`, but the
rig does not hold still: Odoo's own autovacuum (`ir.cron.progress._gc_cron_progress`) deletes
scheduled-job run rows older than a week. On 2026-10-07 the shared rig held 86,561 job runs while
`volume.json` still said 100,000, and comparisons kept running against it.

## Decision

- `gauntlet/compare/lib/rig-volume.mjs` holds the main lists (model and domain) and a check that
  asks each list for its 100,000th row (Odoo refuses to count chatter above a limit).
- `run.mjs` runs that check against the live rig before any run that includes Odoo, and exits with
  code 2 without recording anything when a list is short or the rig cannot be checked. The message
  names the short lists and the top-up command (`up.sh`).
- The live test (`test/live-odoo.test.mjs`) uses the same module, so the two cannot drift.
- Disabling Odoo's vacuum was rejected: it would change the reference's behaviour. Dating seeded
  rows ahead of time was rejected: the reference would hold records no real Odoo could hold.
  Topping up with `up.sh` is the honest fix; it is idempotent and, on the shared rig, leaves the
  running containers alone (the compose configuration hash is unchanged, so `up -d` is a no-op).

## Consequences

An Odoo run costs one sign-in and seven indexed searches more (a few seconds). A week after the last
top-up, Odoo runs stop with a clear message until someone runs `up.sh`. The rig is not a dependency
of `./erp verify`; the check's tests use a stand-in Odoo.
