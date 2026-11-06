# p09 — What the round adds to the shared gates

Date: 2026-11-06. Piece: p09-custom-fields, round 1. Status: accepted.

Endpoints added (each is attacked by G1 and G2): 5, all under `/api/customfields/`:
`GET /fields`, `GET /fields/{id}`, `POST /fields`, `PUT /fields/{id}`, `GET /record-types`; plus the
standard saved-view endpoints of one new list (`customfields.fields`, 9 more, generated). No value
endpoint exists: values travel with the contacts endpoints. Endpoints changed: the contact create,
update, get and defaults carry `customFields` (documented body and answer fields), and every list
definition endpoint adds the workspace's fields as columns.

List columns added to the gates' reach: 9 filterable and sortable columns of the definitions list
(in memory, 4 groupable). The custom fields themselves are columns only for a workspace that defines
them, and are not sortable or groupable; the gate workspaces define 4 fields on contacts (3 filterable
text/select/date/flag columns beyond the contact's own), so the contacts list gains 4 filter attacks
and its definition 4 columns.

Data added to the gate workspaces: 4 definitions and 2 to 4 values per contact (25 contacts a tenant;
more at the list-volume gates), each audited twice (the capture trigger and the record history).
The demo adds 4 definitions and about 270,000 values on the 100,000 contacts.

Processor time: measured by the verify run recorded in the builder's notes; a new piece's first
integration may raise the maximum by at most 3,500 s (owner decision #34).

## Round 1, fixer 2: the audit log print and export asked the same questions on every page

Date: 2026-11-06. Failure being fixed: `G1HttpIsolationTests` aborted on `audit.log?format=pdf&language=ar...`
at 100.3 s (fixer 1's verify of 0c6ad0f2, load 25 to 50). The twenty slowest requests were audit.log
exports at 30 to 93 s, but also one plain `GET /api/audit/log?take=50&filter=fields eq 'expires_at'` at
29.9 s and json exports at 30 to 37 s, so part of the time was the host stalling under load. p09 adds
few audit rows (2 field definitions, values on 6 contacts); it is not the cause, but the print and
export is the longest path of the gate, and p09's gate-cost budget is better spent shortening it.

Cause found by reading the code (not profiled: the gate takes 2 h and the machine was shared): the
reports framework reads the log in pages of 200 rows, and `AuditReader` repeated work in every page:
`ReadableTypesAsync` asked the permission store for every audited kind of record, twice a page (the
page's own `VisibleAsync` and the one inside `ReferencedNamesAsync`), and `ReferencedNamesAsync` ran
its name query (ordered by time, stopped at 2,000 matches, so a scan of the tenant's log when few ids
match) again for the same company, role and user ids that recur in every page.

Change (src/Modules/Audit/Erp.Modules.Audit/AuditReader.cs): the reader is scoped to one request, so it
keeps (1) the readable kinds of record and (2) every id it has already looked up, found or not. A
printed log then asks the permission store once and reads each distinct id once. Results are
unchanged. No test, limit or timeout was changed.

### The same failure again in fixer 2's verify, and the larger cause

Verify of d445a4ec (log verify-fixer-p09-custom-fields-r1-f2.log, kept folder
.verify-failed/20261106T160250Z-2441416): web and e2e passed (web 5,227 s wall, e2e 9,435 s wall), dotnet
failed after 8,488 s with the same test: audit.log pdf at 167.8 s. The gate's own list walk shows the
larger gate tenant holds more than 820,000 audit rows (`skip=410182` is half of it), and a plain
`GET /lists/...` route probe took 90 s in the same minutes, so the host was starved while several
exports ran. (Another piece's verify ran beside this one for the first two hours; load 25 to 75.)

Larger cause, found in code: a print or export of a list reads it in pages of 200 rows up to the file
limit (200,000 rows: 1,000 pages), and `ListBinding.QueryAsync` ran `COUNT(*)` of all matching rows on
every page (with row-level security and the audit log's visibility filter over 820,000 rows). The
reports framework only uses the total of the first page.

Change: `ListRequest.SkipTotalItem` (an `HttpContext.Items` key, not a query parameter, so no client can
send it and no list endpoint gains a parameter). The reports framework sets it for every page after the
first; `QueryAsync` then leaves the count out of a cursor page (Total 0). Screens and the API never set
it, so their totals are unchanged. This is kernel (p05) and reports code, changed minimally because it is
the cause; the first page of a document still counts and the document's total is unchanged.
