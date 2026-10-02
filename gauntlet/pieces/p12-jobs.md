# p12 — Background jobs

- Durable job queue in PostgreSQL; jobs run inside their tenant's context under row-level
  security; retries with backoff; scheduled (cron) jobs; progress reporting; cancellation.
- Job monitor screen: list, filter, see progress, errors and output, rerun.
- Jobs and their outputs are tenant-isolated (G1 attacks job ids) and permissioned (G2).

Compared against Odoo: see the state of a job and run it again.
