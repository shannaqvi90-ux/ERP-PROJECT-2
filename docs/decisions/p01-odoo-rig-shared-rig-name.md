# p01 — The shared Odoo rig: project `odoo-reference`, external volumes, adopted in place

Date: 2026-10-03. Piece: p01-odoo-rig. Status: accepted.

## Context

The shared rig ran under the compose project `b-p01-odoo-rig`, the same prefix builders use for
their own stacks. At the end of round 2 a builder clean-up rule (`docker compose -p b-p01-odoo-rig
down -v`) matched it and deleted its volumes; the lead had to reseed 100,000 rows per list.

## Decision

- **Default project `odoo-reference`.** No builder or critic project rule (`b-<piece>`,
  `c-<piece>-r<n>`) matches it.
- **External volumes for the shared rig.** `up.sh` and `down.sh` add `compose.shared.yaml` when the
  project is `odoo-reference`. It names the data volumes `odoo-reference-db` and
  `odoo-reference-filestore` and marks them external. `docker compose down -v` never removes an
  external volume, whatever project name it is run with. `up.sh` creates the volumes when they are
  missing. They carry no compose project label, so no label-based clean-up selects them.
- **Deleting the shared data needs a confirmation.** `down.sh --purge` on the shared rig refuses
  unless `ODOO_REF_CONFIRM_PURGE=odoo-reference` is set. A private copy (any other
  `ODOO_REF_PROJECT`, for example a critic's own rig on another port) keeps ordinary
  project-scoped volumes, so its own `down -v` still frees its disk.
- **Adopted in place, not reseeded.** `up.sh` (`rig.sh: adopt_legacy_rig`) finds the legacy
  volumes `b-p01-odoo-rig_db` and `b-p01-odoo-rig_filestore`. It stops the old Odoo and then the
  old PostgreSQL container (a clean shutdown with a checkpoint). It moves the data directories into
  the new volumes with a rename on the same file system: instant, with no second 2.7 GB copy. Where
  the host cannot reach Docker's volume directories it falls back to a copy in a helper container.
  It then removes the stopped old containers and the emptied old volumes, and starts the rig under
  the new name. There is never a second rig on port 8069.

## Evidence

On 2026-10-03 at 03:28 UTC the adoption ran on this machine in 32 s. Every seeding step reported
+0 rows: contacts 100,000; users 100,005; rates 100,000; audit messages 100,007; attachments
100,046; job runs 100,003; purchase orders 100,000 with 200 waiting. Those are the counts the lead's
02:20 UTC reseed produced (`gauntlet/reference/odoo/volume.json`).

## Consequences

- Critics and builders must still never stop the shared rig. If something stops it anyway, the data
  survives and `tools/odoo-reference/up.sh` brings it back in seconds.
- The seed's `setup` step now reports +0 (it writes configuration, not rows), so a second run of
  `up.sh` reports +0 on every step.
