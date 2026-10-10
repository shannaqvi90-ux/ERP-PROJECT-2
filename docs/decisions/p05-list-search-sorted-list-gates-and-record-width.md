# p05 — sorted list queries in G1, own-view writes in G2, and a resizable record form (round 8)

Date: 2026-10-09. Piece: p05-list-search (round 8). Status: accepted.

## Context

Critic p05 round 7 planted L11: a C# file-local class in `ListBinding.cs` kept list totals in a
static dictionary keyed by list, search, filter and sort, with no tenant, and answered a request
carrying both a sort and a search with the first total counted. All 53 G1 tests passed:

- `ListAnswers`, the only gate that judges what a total adds up to, never sent `sort`, although
  the client sends it on every header click. `HttpIsolation` and `ListContract` sent sorts, but
  never with a search and never judging the total. `NonInterference` cannot see statics.
- The process-state gates skipped every type whose name contains `<`, which includes C#
  file-local types. That part was routed to p00, which fixed it on the integration branch
  (`Infrastructure/CompilerGenerated.cs`, merged here; this piece adds no second mechanism).

The critic's P6 (a personal default view also cleared the workspace's shared default) passed all
36 G2 tests, and P7 (`GET /views` listed every user's personal views) was caught only by accident.

Routed from p03 rounds 6 and 7: a screen's record form was 340 px, then 48% (806 px at 1920 px),
and a user's "Workspace and companies" table, about 1,050 px wide, still ran past the panel.

## Decision

1. **Every list query is also judged sorted** (`G1/ListAnswers.cs`). Each kind of query (all
   rows, searches, filters, groupings, groupings with a search) is sent again with a sort, so that
   every query is sorted once and every sort key (each sortable column, ascending and descending)
   meets every kind of query at least once; two keys at once are sent alone and with a search.
   Each sorted query is walked exactly like an unsorted one (both tenants in lock step, keyset and
   offset pages, every page's total and groups judged against the tenant's own rows), and a
   sorted walk must return exactly the rows of the same query unsorted. A list with sortable
   columns is a blind spot unless some sorted query, and some sorted search, has different true
   answers in the two tenants, and some sorted query spans several keyset and offset pages. New
   ratchet minimums: `g1.listAnswerSortedQueries`, `g1.listAnswerSortedDiscriminating`.
2. **NonInterference sends sorted searches** (each sortable column both ways with two search
   texts, one offset page of a sorted search, and a sort with a grouping), so a memo kept in a
   singleton or a captured variable and keyed by sort is compared against a fresh process.
3. **Self-test plant** (`SelfTests/LeakyModule.cs`, list `leaky.sorted`): sorted searches reuse
   the total first counted for the same search, filter and sort, whatever its tenant. The list
   answer self-test must catch it in both directions and only for requests with both a sort and a
   search; the non-interference self-test must catch it the same way.
4. **G2 own-view writes and view listings** (`G2PermissionTests`, list endpoint test). The
   administrator's personal and shared views are both defaults before the checks. A reader
   without the share permission and a sharer each save three personal views and change each twice,
   with every body field set and `isDefault` both true and false; the administrator's snapshot
   (names, versions, default flags) must not change. `GET /views` for the administrator, the
   sharer and a reader must list shared views and the caller's own personal views only. New
   ratchet minimums: `g2.listOwnViewWrites` (90), `g2.listViewListingsJudged` (15). Checked against
   the critic's plants: P6 alone fails the test (20 changes to the administrator's views), P7 alone
   fails it directly ("listed to a reader a personal view that is not its own"), the product passes.
5. **The record form is resizable and fits its content** (`kernel/lists/recordWidth.ts`,
   `ListView.tsx`, `lists.css`). A splitter (an ARIA window splitter: Tab to it, arrow keys, Home,
   End, Enter, or drag; double click) sits between the list and a screen's form; Alt+W anywhere
   switches the form between the standard 48% and the widest 75%. Until the user chooses, a form
   whose content is wider than the panel grows by itself just enough to show it, up to 75%. The
   list always keeps at least 288 px. The user's width is kept per list in the browser's local
   storage, read and written inside try/catch (a convenience only; without storage the form opens
   at the standard width and fits its content). The tenancy screens' own 48% override is removed so
   every screen sizes its form the same way (it still stacks below 1,100 px).

## Processor time

Measured on the G1 HTTP attack run alone (the critic's plant L11 applied, so the test reports its
phase lines), at a load of about 25 to 45 from other agents: the list-answer phase judged 2,472
queries (1,368 of them sorted; 968 and 610 with different true answers in the two tenants) and took
347 s of wall time, against about 65 s in round 6 for 552 unsorted queries. The whole test process
(attack and the app it attacks) used 2,214 processor seconds (user 1,629, sys 585), against 1,914
measured for round 7, so this round adds about 300 processor seconds to the verify. The
NonInterference additions are six requests per sortable column; the G2 additions run in about one
second; no new environment or whole run. Full verifies of this branch (integration branch merged at 9dc5a9a), through the verify slot:
9,765 s (dotnet 8,246) with every test green; 10,797 s (dotnet 9,075) with every test green but
over the 10,500 s maximum, while the self-test's planted sorted list was still a list of its own (its
endpoint and its saved-view endpoints went through every phase of the HTTP attack self-test, about a
quarter of an hour of the verify's longest process); after moving that plant onto the existing
planted scroll list and judging only the planted lists sorted in the self-test, 9,844 s (dotnet
8,246, web 1,269, e2e 272, timing 57) with every test green and the ratchet passing, at a load of
about 25 to 45. The integration branch's last quiet verify was 8,694 s (dotnet 7,359), so this round
adds roughly 900 to 1,100 processor seconds at that load, most of it the sorted list answers in the
gate and in its self-test. The margin under the maximum is about 650 s.

The plant run caught L11 twice over: 5,965 list answers
were wrong (for example "GET /api/tenancy/companies?take=50&search=a&sort=code answered total 49,
but walking the same query returns 26 rows") and the process-state check saw the static memo
change (p00's file-local fix).

## Rejected

- Every sort key crossed with every query (the critic's first suggestion taken literally): about
  ten times the list-answer phase, for no case the per-kind rotation misses (a memo is keyed by
  the request; the rotation sends every key with every kind of query and every query with a key).
- A second process-state mechanism for file-local types: p00 owns it and fixed it.
- Opening the record full-screen instead of resizing: it hides the list, and the bar asks to keep
  the dense list beside the record.
