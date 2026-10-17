# p05 — list gates at demo volume (round 10)

Date: 2026-10-17. Piece: p05-list-search (round 10). Status: accepted.

## Context

The round 9 critic planted L12: in `ListBinding.QueryAsync`, a list with no search and no filter
over a table of more than 10,000 rows answered `pg_class.reltuples` as its total, the planner's
row count of the whole table, every tenant's rows. All 56 G1 tests passed, because every gate
environment seeds 25 users per tenant (`SeedPlan.Gate`): the list answer gates (`ListAnswers`,
non-interference, the HTTP attack) judge totals carefully, but never at a volume where code for big
lists runs. Live, gulfsteel (1,005 users) saw 101,010.

Since then p00 (rounds 9 and 10) closed the database side: the bootstrap takes the statistics
views, the `pg_stat_get_*` and size functions from PUBLIC, `erp_app` cannot read pg_class's row and
page estimates, and a static gate bans whole-table figures in product code. L12 as written now
fails (the application role is refused reltuples). That does not close the gap: other volume
paths (a planner estimate from `EXPLAIN`, a total kept for queries too big to count every time, a
deep page served from a remembered position, a broad search answered differently once it is too
big to rank) still only run past a threshold the gates never reach. The lead's note: build on p00's
mechanism, do not add a second; make the list gates exercise volume paths cheaply.

## Decision

1. **A large-list profile for the list gates.** `SeedPlan.GateAtVolume(canary, password, a, b)`
   is the gate fixture with a volume per tenant (`SeedPlan.Gate` is now `GateAtVolume(…, 25, 25)`,
   unchanged). `G1ListVolumeTests` seeds tenant A with the demo volume (`SeedPlan.DemoVolume`,
   100,000, the constant the demo profile now reads too) and tenant B with 2,500 more, so every
   whole-list answer differs between them, and runs `ANALYZE` so the planner statistics are fresh
   (an estimate then sits near the whole table's 202,510 rows, never near one tenant's).
   Both tenants are big: a total memo used only above a threshold on the tenant's own count is
   only read across tenants when both pass it (critic's fix a and c).

2. **`ListVolume`: every registered list judged against each tenant's own rows.** Exhaustive
   keyset walks at 100,000 rows per query would cost far too much, so the truth comes from one walk
   per tenant and list: each tenant walks the whole list in its default order (take 200, lock step
   with the other tenant, every page's total judged), which gives every row with its values. The
   walk is anchored in the database: the tenant's row count of the list's table (read as the
   superuser, `WHERE tenant_id = …`) equals the walk, and every walked id is a row of that tenant.
   Then a query set built from the definition (everything; every sort key both ways; every choice
   and flag value; text filters with a common letter, a rare letter and an Arabic letter; `is null`
   on every filterable column; every grouping alone, sorted and filtered; fourteen one-letter
   searches, Latin and Arabic, chosen so some match more than `ListSearch.MaxRankedRows` rows (the
   unranked path) and some fewer (ranked); searches sorted and grouped) is answered for each tenant
   from the walked rows, and every query is sent by B, A, B and A (each answer right after the
   other tenant's identical request, both ways round) as a first page (take 50) and at offsets
   (take 100, the client's jump chunk): half way, the last rows, past the ranking limit, and past
   the end for the whole list. Every answer's total must be the tenant's own matching rows, its rows
   must be among them (exactly the walk's slice where the default order applies), and its groups
   must count them. Deep keyset cursors of each walk are followed by both tenants. Blind-spot checks
   per list at volume: discriminating queries, deep answers, an unranked and a ranked search.
   Measured: 285 queries, 2,916 answers, 840 of them past offset 10,000; users and access lists at
   volume (405,040 rows walked).

3. **Whole-table figures in the SQL trace and the static ban.** `SqlTrace` records every statement
   sent inside a request that reads the statistics views, pg_class's estimates, the size functions,
   or runs `EXPLAIN` (cheap substring tests first, the expression on almost no statement). The
   volume gate and the G1 HTTP attack both fail on any (critic's fix b, built on p00's ban, not a
   second mechanism). p00's static ban on product code now also names `EXPLAIN` (upper case, or
   a string starting with `explain`): its row estimates come from the same whole-table statistics.

4. **Self-tests.** `VolumePlantModule`, on a host over the volume database (`OverHost`), changes
   the users list's total only past 10,000 rows: V1 reltuples for whole-list queries (L12), V2 a
   total memo keyed by the query for filtered ones, V3 the `EXPLAIN` estimate for searches. The
   volume check reports each and the trace reports the pg_class read and the `EXPLAIN`. None of
   them can be seen by the 25-row gates.

5. **The client at volume.** The client isolation unit test (B then A in one tab, English and
   Arabic) serves each tenant's users list at demo volume (100,005 and 102,505): the journey clears
   the search and jumps to the last row, which the list loads by offset deep in the list; B's count
   is a marker A's tab must never show, and both tenants' deep loads are asserted.

6. **G2: view bodies from the schemas (plant P8).** The list view check sent bodies built from a
   fixed field list, so an `isShared` field honoured by `POST /views` (which declares only the
   list's read permission) let a reader create the workspace's shared default. The check now sends,
   on both personal routes, the full body plus each property of every view endpoint's request
   schema and of the view GET answers (`isShared`, `isMine`, ids, …) with every value of its type,
   for a reader without the share permission and for a sharer: the answer must stay the caller's
   own personal view and the workspace's views must not change. Self-test: `SharedViewBypassModule`
   (P8's behaviour on a planted host) is reported. 292 field variants.

## Cost

- Seeding 2 × 100,000 users is a bulk copy (the product's own seeder) plus `ANALYZE`.
- The two volume tests, run alone on this machine: 4 min 15 s wall, 65 processor seconds of the
  test process (56.5 user, 8.1 system), including the host start and seeding. PostgreSQL's own
  time is in the shared test server's container, outside the verify's processor count.
- The client isolation test: about 6 s more wall time (waits), negligible processor time.
- G2 field variants: under a second.
- Full verify of this branch at 7dc7dbb (slot taken 03:12:49Z, load up to 83 from other agents):
  passed in 3,918 s wall, **9,682 processor seconds** (dotnet 7,689, web 1,709, e2e 249, timing 36;
  maximum 10,500). The integration branch's last verify before this round (p02 round 10's
  integrator) used 9,808 s (dotnet 7,793): this round's added gate work is inside the run-to-run
  noise, about 70 processor seconds by the standalone measurement above. Tests: .NET 615, web unit
  477, end-to-end 102, comparison 373, build 6.

## Rejected

- Seeding volume into the shared gate template: every gate environment would copy 200,000 users,
  multiplying the cost by the number of environments.
- Exhaustive keyset walks per query at volume: about 100 times the requests.
- Running the volume check in the timing stage's 100,000-user fixture: that fixture lives in the
  identity test assembly and is measured alone for the timing budgets; gate checks there would
  disturb the measurement and live outside the gate project.
