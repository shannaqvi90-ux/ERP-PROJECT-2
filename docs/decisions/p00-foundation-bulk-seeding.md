# p00 — Bulk seeding through a staging table, under row-level security

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Decision

- `BulkInsert.InsertAsync` binary-`COPY`s rows into a transaction-local temp table, then moves them
  with one `INSERT … SELECT` into the target. That statement runs as `erp_app` inside the tenant's
  transaction, so the RLS `WITH CHECK` validates every row and the audit trigger records every row.
- Seeders implement `ITenantSeeder` (ordered, idempotent). `SeedRunner` binds one scope and one
  transaction per tenant with actor kind `seed`. Profiles: `demo` (Al Noor Trading with
  `Erp:Seed:Volume` users, default 100,000, and a smaller second tenant), `minimal`, and `gate`
  (two tenants; tenant B weaves a random canary into every text value so any leak is detectable).
- Demo data is generated deterministically in code (UAE workforce names, Arabic and English); no
  generated data files are committed.

## Why

- PostgreSQL refuses `COPY FROM` into a table with RLS for non-bypass roles; the staging hop keeps
  COPY speed (100,000 rows in seconds) without granting any bypass.
- Seeding through the same role, RLS and audit as user writes means the demo cannot contain data
  the product itself could not have written.
