# p00: G1 sends tenant B's own requests as tenant A, and the app role reads no whole-table statistics

Date: 2026-10-10 (round 9). Piece: p00-foundation. Status: accepted.

## 1. Answer shapes: tenant B's requests, sent by tenant A exactly as sent

### Context (critic p06 round 5, routed to p00)

The answer-shapes phase of the HTTP attack (`tests/Erp.Gates.Tests/G1/AnswerShapes.cs`) had tenant
B ask for every shape of every GET whose API document enumerates how it answers (format, language,
digits, disposition, grouping) on B's own records, then tenant A ask for the same shapes **on A's
own records**, then B again. Two blind spots:

- **B's record ids never travelled with a non-default shape from A.** Plant L7b: a record PDF kept
  for the inline viewer by the record id and the disposition, with no tenant, served from the store
  before the record is looked up. A asking for its own records inline gets its own PDF; A asking
  for B's record without `disposition=inline` (the route attack of phase 1) gets 404. Only A sending
  B's record id **with** `disposition=inline` gets B's PDF, and no phase did that. The same holds
  for any store keyed on two inputs together, in any module.
- **Documented values went one per request.** Plant L7c: a spool keyed on the columns and the sort
  together. B never sent both at once, so B never filled it.

### Decision

- During the phase, besides its own shapes, **tenant A sends every request tenant B made, exactly
  as B made it**: B's record ids in the route and the query, every enumerated shape, every
  documented value, B's own query text. Whatever a store keyed on the request (any function of it)
  without the tenant holds for those requests is B's, and A's answer is judged for B's markers like
  every other answer. The ids A sends are passed to the judge as sent values (an answer that echoes
  an id back, such as a 404 problem naming it, is not a leak); anything else of B's is. A request of
  B's that is exactly one A sends anyway (a route that names no record) is sent once.
- **Documented values together.** After the accepted documented values one at a time (each with
  every value of every enumerated parameter), tenant B also asks with every documented parameter's
  values together: row *k* gives each parameter its *k*-th accepted value (cycling through the
  parameters with fewer), one row per value of the parameter with the most, each row with a
  different row of enumerated values. A refusal of the combined row is a recorded blind spot.
- New counters, checked against the ratchet: `g1.shapeReplays` (B's requests A sent as sent) and
  `g1.shapeCombinedRows` (shapes in which B sent its documented values together).
- **Plants in the HTTP self-test** (`LeakyModule`): `/api/leaky/viewed` (L7b: user id, format,
  disposition; the inline store answered before the lookup) and `/api/leaky/combined` (L7c: the
  columns and the sort together). `GateSelfTests.The_HTTP_attack_catches_planted_header_route_and_body_leaks`
  requires a leak on each, found by the replay and by the combined rows respectively.

This is one mechanism in the shared phase, so it covers every module's GET with enumerated
parameters, not only reports. The reports module's own isolation probe (`ReportsIsolationProbe`,
p06) is p06's; the replay covers the same report routes from the gate side.

### Routed item p03 r9 (documented query parameters in the takeover gate)

The shared request-building code does not drop query parameters: `ApiEndpoint.Path` builds the
route only, and each gate composes its own query. The takeover gate (`G2AccountTakeoverTests`,
p03) sends the path and the body only. The machinery to enumerate documented values exists and is
shared within the gate assembly: `IsolationAttack.DocumentedValues(openApi.Parameters(method,
pattern))` gives a value other than the default for every documented query parameter (both answers
of a yes/no flag such as `removePasskeys`, examples, dates, a time zone), so p03 can send them from
its own gate without new shared code.

## 2. Statistics that count every tenant

### Context (critic p00 round 8, other finding)

PostgreSQL's statistics views and functions (`pg_stat_user_tables.n_live_tup`, `pg_stat_activity`,
`pg_stats`, `pg_stat_get_live_tuples()`, `pg_total_relation_size()` …) are readable by PUBLIC and
describe whole tables: every tenant's rows together. Plant L3 (a list total from `n_live_tup`) was
caught only by the HTTP attack's list-walk check, and the non-interference check cannot see such a
channel at all (both of its processes read the same database).

### Decision

- **The bootstrap takes them from PUBLIC** (`KernelSql.RevokeCrossTenantStatistics`, run by the
  superuser bootstrap in the product's database): every `pg_catalog` view or table named
  `pg_stat*`/`pg_statio*` (row counts, activity, other sessions and their query text, `pg_stats`
  and `pg_stats_ext`, which sample column values of every tenant), every `pg_stat_get_*` function
  and the size functions (`pg_relation_size`, `pg_total_relation_size`, `pg_table_size`,
  `pg_indexes_size`, `pg_database_size`, `pg_tablespace_size`). The superuser keeps them.
- **Gate** (`G1DatabaseIsolationTests.Application_role_cannot_read_statistics_that_count_every_tenant`):
  from the catalogs, the application role can read none of those views (at least 40 found) and run
  none of those functions (at least 100 found); as `erp_app`, six representative queries are refused
  with `insufficient_privilege`.
- **Static ban** (`Product_code_never_reads_statistics_that_count_every_tenant`): no product source
  (`src/**/*.cs`, `*.sql`) names the statistics views, the live/dead tuple counters, `pg_class`'s
  `reltuples`/`relpages`/`relallvisible` or the size functions; only the bootstrap SQL that revokes
  them may. `pg_class` itself stays readable (every client library reads it), so its estimates are
  closed by the ban, not by a privilege.
