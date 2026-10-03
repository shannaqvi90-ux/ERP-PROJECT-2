# p00 — Audit trail written by a database trigger

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Decision

- `audit.entries` (kernel-owned, tenant-owned, RLS forced) stores: tenant, time, actor id, actor
  kind (`user`, `seed`, `job`, `system`), schema, table, record id, action, field-level change set
  `{"column": {"old": …, "new": …}}`, PostgreSQL transaction id and the request's correlation id.
- Rows are written by the `AFTER INSERT OR UPDATE OR DELETE FOR EACH ROW` trigger
  `audit.capture()`, attached by `ProtectTenantTable` to every tenant table. The actor and
  correlation id come from transaction-local settings set by `ErpDbSession.BeginAsync`.
- Updates that change nothing are not recorded; unchanged fields are left out of the change set.
  Secret columns are recorded as `"[redacted]"` (password hash); high-churn bookkeeping columns can
  be left out (last sign-in time, lockout counter), each named in the migration.
- The app role may `SELECT` and `INSERT` its own tenant's audit rows, never `UPDATE`, `DELETE` or
  `TRUNCATE` them (append-only). The gate checks this.
- Exemptions (the audit table itself, the sessions table which is itself the sign-in history) are
  listed with reasons in `tests/Gates/audit-exempt.txt`; the ratchet caps their number.

## Why

- CLAUDE.md rule 4 says *every* business record. A trigger catches every write path — EF, raw SQL,
  bulk insert, future jobs and imports — in the same transaction, so audit can neither be skipped
  nor diverge from the data. An EF `SaveChanges` interceptor would miss `ExecuteUpdate`, raw SQL
  and COPY-based imports.
- p07 builds the screens on top of this table.

## Trade-off

- Bulk loads write one audit row per row (100,000 users ≈ 100,000 audit rows). Accepted: the demo
  seed still completes in well under a minute, and imports must be audited anyway.
