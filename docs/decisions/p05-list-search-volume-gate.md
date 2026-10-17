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

## Round 11

The round 10 branch (ede7b07) could not be integrated because the integrator's verify never got
a slot, so it never ran. This round merged the integration branch (16d55bd: only
`gauntlet/needs-human.md` and `gauntlet/runlog.md` changed) and re-ran the full verify. No product
or gate code changed, so this round adds no processor time. Full `./erp verify` of 8aeed05
through the verify slot (slot taken 05:18:30Z, load up to 78.6 from other agents) passed in
3,845 s wall and **9,638 processor seconds** (dotnet 7,632, web 1,704, e2e 266, timing 38;
maximum 10,500). Tests: .NET 615, web unit 477, end-to-end 102, comparison 373, build 6, each
equal to its ratchet minimum.

## Round 12: the volume gate's tallies made deterministic

The round 11 branch merged onto the integration branch and its integrator's verify failed one
gate: `g1.listVolumeRankedSearches: 5; ratchet minimum 6`, with no wrong answer, no blind list and
no whole-table read. The cause: tenant B's code is `bravo` plus the run's fresh canary, and the
identity seeder drew B's generated people from that code, so B's matches per search letter changed
from run to run. A search counts as ranked only when its true counts differ between the tenants
and both lie between 1 and `ListSearch.MaxRankedRows`; 'g' could tie A's count exactly, 'w' sat
either side of 10,000. The minimum 6 had been set from a single run.

Decision:

1. `SeedTenant` gains an optional `VolumeSeed` (`VolumeRowsSeed` falls back to the code, so every
   existing plan and the demo are unchanged). `SeedPlan.GateAtVolume` sets tenant B's to the fixed
   `SeedPlan.GateVolumeSeedB` ("bravo"); the identity seeder draws generated people from
   `VolumeRowsSeed`. The canary still marks every text value of B (names, Arabic names, e-mail
   domain, code), so a leak stays visible. `SeedPlan.Gate` (25 rows) keeps drawing B's people from
   its code: the other gates' figures were set on it and this round does not move them.
   The run's canary was the other random input: its hexadecimal digits put a to f into every
   name and the e-mail domain of B, so 'contains a' filters and the 'a' searches changed between
   runs (this round's first verify gave 82 discriminating queries, 2,916 answers and 840 deep
   answers where a run alone had given 86, 2,944 and 852). The volume fixture's canary is now
   `CNRY` and twelve random decimal digits (`G1VolumeFixture.NewCanary`, still fresh per run);
   c, n, r and y are none of the letters the check filters or searches by
   (`ListVolume.CheckedLetters`). The other gates keep `ErpTestEnvironment.NewCanary`.
2. The volume summary prints each one-letter search's true counts in A and B, so a later change of
   the seed or of the letters shows its effect in the log.
3. Self-tests: the identity tests check that the volume plan's B seed is the same whatever the
   canary, carries no canary and generates the same people, and that plans without a volume seed
   use the code; `G1ListVolumeDeterminismTests` draws 200 fixture canaries and checks each is
   unique, holds none of the checked letters and leaves B's people seed fixed.
4. Ratchet minimums set to the fixed figures (two runs of the gate alone with the decimal canary
   gave the same figures; the same per-letter counts came from a simulation of the generator):
   ranked searches 6 to 7 (users: g 5,168/5,208, j 6,472/6,454, q 9,480/9,636, ق 1,951/2,003;
   access: g, j, q), unranked searches 15 to 16, discriminating queries 80 to 82, answers 2,900 to
   2,916, deep answers 800 to 840. Every ranked search's two counts differ by at least 18 rows.

Rejected: choosing other search letters while B's people still came from the canary (counts near
a boundary would still move between runs), and lowering the minimum (CLAUDE.md rule 9).

Cost: no new environment, run or request; the seeding is the same bulk copy. The volume gate run
alone took 2 min 9 s for the test (2.9 min with build and start).

Full `./erp verify` of 626452d (the integration branch merged in, 37fd25b) through the verify
slot: slot taken 20:53:57Z after waiting 3,315 s, passed in 3,668 s wall, **9,901 processor
seconds** (dotnet 7,574, web 1,942, e2e 326, timing 59; maximum 10,500), with load up to 78.9 from
other agents. Tests: .NET 641 (the ratchet minimum raised from 635), web unit 503, end-to-end 106,
comparison 415, build 6. This round's own processor cost is the 201 one-line identity and
self-test checks (well under a second); the volume gate runs the same requests as before. The
first verify of this round (262d8a3, hexadecimal canary) failed only on the minimums this round
had raised from a single run alone, which is how the canary's letters were found.
