# p00-foundation round 18: two gate false positives, the list bound, trailing slash, gate memory

Routed round (serial-17). No product feature. Every change is a gate change; none weakens one:
no endpoint, attacker, carrier, value kind or oracle is dropped.

## 1. A list that moves while a pair is asked (G1CompanyIsolationTests)

Round 17: `GET /api/audit/log?take=20002` and its control answered different 200-row windows
because the audit log gained rows between the asks, and the repeat of the pair differed as well.

Decision (`Infrastructure/MovingLists.cs`, used by `CompanyAttack.State.Compare`): when a pair
differs twice, the four answers (answer, control, answer, control, in the order asked) are judged
as a time series. A list that moves changes with time only, so each row that differs is absent
then present or present then absent (one change in four answers), a number outside the lists (a
total) only grows or only shrinks, and the rows all four answers hold keep one order. An oracle
follows the side instead: a row shown to company Y's value alone is present, absent, present,
absent; a count one higher for Y goes up and down; a reordering alternates. Anything else (another
status, a text that is not JSON) was never explained and still is not. Chosen over "compare only
rows written before the pair" because rows carry no common timestamp field, and over "hold the
writers still" because the job runner and the product's own audit writes are not the gate's to stop.

Self-tests (`SelfTests/MovingListsSelfTests`): a sliding window with a growing total is not an
oracle; a Y-only row, a row hidden from Y, a count one higher and a reordering are caught on a
list that moves as well.

## 2. A product's own text is not a marker (G1HttpIsolationTests)

`p12`'s job type name `تنظيف المهام المنتهية` is shipped to every tenant (resource `job.type.jobs.cleanup`)
and was also tenant A's seeded schedule name; `VictimValues` dropped a marker only when the other
tenant's rows held it, so the verdict depended on the seed draw.

Decision (`Infrastructure/ProductStrings.cs`, `VictimValues.ReadAsync`): a B-only text of 16
characters or more is left out of the marker set when it equals (trimmed, any letter case) a
string value in `src/**/Resources/*.json` or `web/src/**/i18n/*.json`, the text the server ships.
A value that merely contains such a string, plus anything else, stays a marker. The value is still
sent as an attack value; it only cannot prove a leak. The "answer for a freshly created empty
tenant" source named in the ruling was not added: the resource and web strings cover every case
seen and an empty-tenant read would cost a tenant set-up for no further text. The count of
product texts left out is printed in the gate's phase line ("N product texts left out") so the
effect on any database is visible. On the integration branch's demo this is measured in the
round's verify (see Measurements).

Self-tests: the Arabic clean-up text is product text; a tenant value containing it, plus other
words, is kept; trimming and case are ignored.

## 3. The whole-list walk's bound

`ListAnswers.WalkTogetherAsync` stopped after `maxPages` pages (2 x volume / 200 + 1000). It now
stops after the larger of that floor and `2 x (largest total any page answered) / take + 1000`, so
it follows each list's real size (p16's 100,000 audited contacts included) and still ends a cursor
that never ends. Every row is still walked and judged; a page whose total differs from the walk's
count is still reported. Self-tests: `ListWalkBoundSelfTests` (a 3,000-page list walks to its end
under a floor of 5; an endless cursor stops).

## 4. Trailing slash (gate backlog row 15, plant P17)

`G2PathCaseTests.DeniedAsync` sends, for the caller who must be refused, one more request per
permissioned endpoint: the same request with a `/` added to its path (before the query). Cost: one
request per endpoint per run of the deny, 204 requests (about the cost of the varied-case deny:
under 10 s wall). Plant P17 (`PathCaseModule.Switch.SlashOn`, a handler that passes any signed-in
request whose path ends in '/') is caught on all endpoints while the lower-case deny matrix stays
clean (self-test `The_trailing_slash_deny_catches_a_permission_check_that_tests_how_the_path_ends`).
Ratchet: `g2.trailingSlashDenyRequests` 200 and `g2.trailingSlashPlantsCaught` 1 added.

## 5. Gate process memory

Cause: `SqlTrace` kept every record of a run (bindings, setting statements, setting changes,
read-only marks, reviewed-function calls) in static `ConcurrentQueue`s for the life of the
process, across every attack class; round 15 measured 2 GB of 2.8 GB for the self-http process
and the trace queues as most of the 16 to 19 GB the suite process holds.

Decision: the records live in files. `Infrastructure/SpillLog<T>` appends each record as a length-
prefixed binary frame to a temp file that is unlinked at once (nothing is left behind if the
process is killed), buffered in 1 MB, with an index of every 1,024th offset so a read from a mark
(`TraceMark`, positions are still record counts) costs a skip, not a scan. Hot texts (tenant,
endpoint, statement, caller) are written once and numbered (`SpillStrings`, at most 400,000 texts
and 48 M characters in memory); a read gives one shared instance of each, and the paths and
request ids that are not numbered are shared per read. Nothing is sampled or dropped: the rules
read exactly the same records, in the same order, as lists when the gate judges. Cost: one
small binary write per record (a few microseconds against 2.75 ms of processor time per request).
Kept in memory: the `Principals` map (request to the principal's tenant, about 200 MB per million
requests) and the counters.

Not done: running the heaviest attack classes one after another. xunit runs four collections at a
time (`maxParallelThreads` 4); the trace was the only large shared state found, and serialising
would lengthen the wall clock for no processor saving. If the measured peak below is still over
8 GB the next step is that, not sampling.

## Measurements

See the section appended after the round's verify.
