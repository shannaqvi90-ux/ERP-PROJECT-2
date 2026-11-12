> Round 4 note: the tamper-evidence part of this record (audit.capture() as SECURITY DEFINER, INSERT revoked from the
> application role) is NOT carried: it was never integrated, and it conflicts with the integration branch (p09's
> customfields.audit_record_history() inserts into audit.entries as the caller, and a later module's trigger creation
> failed with "permission denied for function audit.capture" on a clean setup). It needs its own round that makes
> p09's function a reviewed definer function too. The print-speed part is superseded by p09's reader caches (d445a4ec).

# p07 round 3: the log prints in seconds, exports carry whole values, only the trigger writes the trail

Date: 2026-11-02. Piece: p07-audit, round 3 (answers the critic of 6614fcb6). Status: accepted.

## Where the time went (measured on the demo, 405,000 audit rows, 200,000-row file limit)

The reports framework reads a list page by page (200 rows) and every page ran the list framework's
full query, which **counts every matching row** (an index scan of 100,000 to 400,000 rows, 60 to
250 ms) and, for each page's values, looked up the names of the records the values mention with a
second query over the whole trail. 2,000 pages times those two statements was the 360 s. The PDF's
line breaking added to it: it shaped the growing line again after every word (about nine shapings of
each line).

Changes, each with its measurement (same machine, same demo, English unless said):

| Output | Before | After |
|---|---|---|
| CSV of 100,000 rows (one record type) | 51 s | 10 s |
| CSV of the whole log (200,000-row limit) | not measured (about 260 s) | 18.7 s |
| XLSX of the whole log | 262 s | 17.9 s |
| PDF of the whole log, English | 360 s | 79 s |
| PDF of the whole log, Arabic, grouped by change, Arabic-Indic digits | 399 s (134 MB, values cut at 200 characters) | 196 s (369 MB, whole values) |

The PDF is bigger and slower per row than a list of short values because it now prints every value
whole (up to the twelve lines a cell holds; before, four lines of a 200-character cut). The users
list prints 100,000 rows in 7 s as Arabic PDF.

- **One count per document** (`ListRequest.ContinuedPage()`, kernel, additive): the pages after the
  first, read by an `after` cursor, do not count the matching rows again. Not a query parameter (an
  internal property), so no endpoint, OpenAPI or gate surface changed. The reports framework uses it
  for the pages of a printed list and of the lists it reads to name ids.
- **Names looked up once per request** (`AuditReader`, scoped): a record id is looked up once, found
  or not, however many pages mention it.
- **Line breaking** (`TextShaper.Wrap`): the words' own widths (each shaped once, words repeat)
  say how many fit; the line is shaped whole and corrected a word up or down, so the break is where
  adding one word at a time puts it, with one or two shapings per line. Test:
  `Wrapping_shapes_about_a_line_or_two_per_line_not_a_string_per_word` bounds the number of strings
  shaped, not seconds. Shaping also builds fewer objects (arrays for clusters, one shared string per
  single character): 4.5 s to 3.6 s for 3,000 Arabic rows in a benchmark.
- **The test that bounds work, not seconds**: `Printing_and_exporting_the_log_count_once_...` counts
  the statements the application sends that read `audit.entries` while a CSV and an XLSX of 700+ rows
  are produced: at most one count, and at most two statements per page.

Not done, and why: **one- and two-letter searches** (`/api/audit/log?search=k`) cost 0.25 to 0.5 s
against 0.07 to 0.12 s for the 100,000-row lists, i.e. about the same per row (the trail has four
times the rows). The count of a pattern the trigram indexes cannot narrow reads the heap of a third
of the table. A covering btree index `(tenant_id, record_label, record_code) INCLUDE (record_type,
record_company_id)` served it index-only in 80 ms instead of 180 ms in a trial; it was not adopted:
it is the twelfth index of the trail's busiest table, paid on every audited write (the demo seeds
400,000), for a 2x gain on a search nobody types on purpose. The row limit of the exports is
unchanged (200,000).

## What the exports and the list now carry

- **Whole values**: the list on screen keeps a line of at most 200 characters (the change panel
  has everything); a printed or exported log (path under `/api/reports`) carries each change's
  whole old and new values.
- **Changed by** as the API shows it: the reports framework prints the label of a reference column
  (`actorName`) when the row has no id but has the label (work the system did: "Demo data"). Generic.
- **Fields**: the names, not a count, for a choice column bound to a list of values that names them
  (`ReportEngine.Cell`); columns of ids (a user's roles) still print the count when the reader
  cannot name them.
- **Moments** in a change text are written as the change panel writes them (4 Oct 2026, 5:31 PM;
  04/10/2026، 5:31 م, Arabic-Indic digits when the document asks), in the workspace's time zone (the
  document's own `timeZone` when it asks). **Permissions** in a role's change are named as the role
  screen names them (the catalogue's words, English and Arabic).

## Tamper evidence: only the trigger writes the trail

Migration `AuditTamperEvidence`: `audit.capture()` becomes `SECURITY DEFINER` with its fixed
`search_path = pg_catalog, pg_temp`, owned by `erp_auth` (the owner of the reviewed definer
functions), which alone holds `INSERT` on `audit.entries`; `erp_app` loses `INSERT`. The row is
written exactly as before (actor and correlation id from the transaction's settings, tenant from the
changed row, time from the clock). `erp_auth` is neither the table's owner nor `BYPASSRLS`, so the
tenant policy still checks every row against the bound tenant. A change made with no tenant bound
(only a superuser could do that before) now fails the policy: the one test that wrote through the
administrator connection binds the tenant first.

- The migrations' role (`erp_owner`) keeps `EXECUTE` on the function: creating a table's audit
  trigger needs it, and it is no longer the owner.
- A third definer function and the ratchet: `g1.securityDefinerFunctions` (maximum 2) counts the
  reviewed cross-tenant lookups and is unchanged. Definer functions that are trigger functions are
  listed in `tests/Gates/security-definer-trigger-only.txt` under their own new maximum
  (`g1.securityDefinerTriggerFunctions`, 1). A function of that kind has no caller or source file to
  review (nothing can call it as a statement); `Trigger_only_definer_functions_cannot_be_called`
  checks in the database that every definer function returning `trigger` is listed, and that neither
  `erp_app` nor PUBLIC can execute it (so it cannot be attached to a table of their own).
  `audit.entries INSERT` is added to the reviewed writes of `erp_auth`.
- Gates stricter: the append-only gate now also refuses a made-up row and a copy of rows back into
  the trail; the module test reads the row the trigger wrote (tenant, actor, time) and shows that the
  application role's forged insert is refused.

## Who sees a record's history (spec: a history panel on every form)

The history on a form is offered to whoever may see histories: a new permission
`audit.history.read` (resource "Record history"), checked by the History endpoint instead of
`audit.log.read`, and by the web extension. It goes with reading records: the Read-only role holds
it (all `.read` permissions), the Company manager role is given it, the administrator has all. On
top of it the kind's own read permission, in the record's company and branch, is checked as before,
so the history shows no more than the record. The whole log (and one change by id) keeps
`audit.log.read`. `AuditPermissions.LogRead`'s comment, the endpoint, the web extension, the
reviewed endpoint map and the tests now say the same. One route still serves every kind (sixteen
routes would cost the isolation gates hundreds of processor seconds, as decided in round 1).
Roles created before this change do not hold the new permission until an administrator adds it.

## The log's sign-in rows

The spec asks for a complete trail with filters, not for rows hidden by default, so the sign-in
sessions stay in the log; the source and record type filters (and the "by source" view) narrow them.

## Gate backlog row 14

`GET /api/audit/log/{id}` is covered: the roles auditor asking for a change of `identity.users`
gets 404 (and 200 for a role's change), a reader limited to company X gets 404 for a branch change
of company Y and 200 for X. With plant P1 (the by-id query without `VisibleAsync`) two module tests
fail (checked).

## Processor time this round adds

Tests: three module tests (a few seconds of one process), two Reports cases, one gate. The isolation
gates send the log's print and export in every shape on every round, which is where the work fell:
those requests cost a count of the trail and a names query per page, now gone.

No shared gate machinery was edited (NonInterference, the G1 generators, the partial-body machinery,
the client-isolation files). `G1ReviewedLookupTests` and `G1DatabaseIsolationTests` got additions only.
