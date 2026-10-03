# p05 — Search on trigram indexes under row-level security

Date: 2026-10-03. Piece: p05-list-search. Status: accepted.

## Decision

- The kernel installs `pg_trgm` (PostgreSQL licence, a trusted extension: the database owner installs
  it without superuser rights) in `public` (migration `ListSearch`).
- A database list indexes its search fields with one GIN index using `gin_trgm_ops`
  (`HasIndex(...).HasMethod("gin").HasOperators("gin_trgm_ops", …)` in the module's model), and each
  sortable column with a B-tree index leading with `(tenant_id, column)` plus the id for keyset steps.
  `ListIndexGateTests` reads `pg_index` and refuses a database list without them.
- The superuser bootstrap marks `pg_catalog.texticlike(text, text)` (the function behind `ILIKE`)
  `LEAKPROOF`. A G1 gate compares the database's leakproof functions with a fresh database's and
  requires every difference to be reviewed in `tests/Gates/leakproof-allowlist.txt` (ratchet maximum 1).

## Why

Row-level security evaluates a query's own conditions after the tenant policy unless they are
leakproof, and only leakproof conditions may be used as index conditions. PostgreSQL does not mark
`texticlike` leakproof, so under our forced row-level security every `ILIKE` search scanned all of the
tenant's rows whatever the indexes: p00's critic measured 196–350 ms at 100,004 users, and with the
trigram index in place but not usable the plan stayed a parallel sequential scan (756 ms on a loaded
machine). After the change the plan is a bitmap scan of the trigram index (`UsersListVolumeTests`
checks the plan and the timings).

`texticlike` has no side effects. Its only errors concern the pattern (a trailing escape character),
never the value it is matched against, and the platform passes patterns only as request parameters,
never from a row, so evaluating it before the tenant policy reveals nothing about other tenants' rows.
The change is limited to that one function and is visible in the reviewed list.

Alternatives considered: a token table matched with `texteq`/`starts_with` (both leakproof): prefix-of-
word search only and a second table per list to keep in step; full-text `tsvector`/`@@` (not leakproof
either, and stems words, which suits prose, not names and codes); a custom leakproof operator and
operator class over pg_trgm's support functions (needs superuser just the same, and far more
machinery); SECURITY DEFINER search functions (each one a reviewed cross-tenant path, against the
ratchet's maximum of two).

## Consequences

- Hosted PostgreSQL services that give no superuser cannot run the `ALTER FUNCTION`; there the
  bootstrap step would fail. Search stays correct without it but scans the tenant's rows. Choosing the
  production database service is a human decision (deployment is a human gate).
- Words of one or two letters contain no trigram; such searches scan the tenant's rows (about 0.1–0.3 s
  at 100,000 rows on this machine) and are still answered.
