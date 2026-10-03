# p00 — Bulk work gets its own connection pool with a long command timeout

Date: 2026-10-03. Piece: p00-foundation (round 4). Status: accepted.

## Context

Critic p00 round 3: `./erp verify` from a clean clone failed twice in a row on a shared 4-CPU
machine. The identity volume fixture seeds 100,000 users and copies their credentials in one
`INSERT … SELECT`; under load that statement (row-level security on every row, one audit row per
row) ran past Npgsql's 30-second default command timeout. A later fix gave that one command a
literal 900 s, and the bulk loader a literal 600 s, but the binary `COPY`, every EF statement of a
seeder and every command a future seeder or importer creates still fell back to 30 s.

## Decision

- `ErpDataSources` builds the application role's two pools: the request pool (Npgsql's default
  30 s, a request that runs longer is a fault) and the **bulk pool**, whose connection string sets
  `Command Timeout` to `Erp:Bulk:CommandTimeoutSeconds` (default 3,600 s; values under 600 are
  refused at start). Both refuse any user but `erp_app`, so row-level security and audit apply
  exactly as before.
- `ErpDbSession.UseBulkConnectionAsync` swaps the unit of work's connection for one from the bulk
  pool, only before anything has used the connection. `SeedRunner` does this for every tenant
  before binding it, so every statement a seeder sends — raw commands, EF `SaveChanges`, `COPY` —
  inherits the long timeout without setting one itself.
- `BulkInsert` sets its statements and its `COPY` to the session's timeout, never less than the
  600 s bulk minimum (so an import inside a request still has room).
- Migrations run with the bulk timeout on the owner connection (index builds and backfills of
  100,000-row tables).
- Gate `BulkTimeoutGateTests`: code on the bulk path (every `ITenantSeeder`, the bulk loader,
  anything that sends `COPY`) may not set a literal command timeout or open its own connection or
  pool; only `ErpDataSources`, the platform and the seed runner build pools; the seed runner chooses
  the bulk pool before binding and seeding. Kernel test `BulkTimeoutTests` seeds through a test
  module whose seeder runs a 35-second statement with no timeout of its own, and checks every
  kind of command on a bulk unit of work inherits the long timeout while requests keep 30 s.

## Why

- One setting at the pool means the guarantee does not depend on every seeder or importer
  remembering a timeout on every command; the gate keeps literal timeouts from creeping back.
- A finite timeout (one hour) still ends a statement that truly hangs.
- Requests keep the short default: a slow request is a bug to find, not to hide.

## Load check

`./erp verify` is run under deliberate CPU contention before handing the branch over (see the
builder's return notes for the exact run).
