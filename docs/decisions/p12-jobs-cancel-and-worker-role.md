
## Round 4, fixer 2: an export counts its selection once (outside this piece, kernel list reader and reports)

Two full verifies (the fixer 1 run and this fixer's first) failed G1HttpIsolationTests on a request
that timed out past 100 s while the slowest requests were all the audit log's PDF/xlsx exports
(30 to 51 s each, run in parallel by the gate), with PostgreSQL busy on `audit.entries` counts.
Cause found in the code: the report framework reads a file export 200 rows at a time and every page
ran `ListBinding.QueryAsync`, which counts the whole selection again (one scan of the audit rows per
page, 500 pages for 100,000 rows). Fix: `ListRequest.ForLaterPage(after, total)` hands the first page's
total to the later pages, and `QueryAsync` skips the count when it has one (the property is internal, so
the API gains no query parameter). `ReportEndpoints` uses it for pages after the first. Test:
`ListEngineTests.A_later_page_of_an_export_keeps_the_total_the_first_page_counted`.

## Round 4, fixer 3: the audit reader asks once per request (outside this piece, p07's reader)

Fixer 2's run B still failed G1HttpIsolationTests on the audit log's exports. The reader, scoped to
one request, asked the permission store for every kind of record on each 200-row page (twice) and
looked up the names a page's values mention again for every page, although the same company, role and
user ids recur. Change (the same as p09's d445a4ec, taken here so the cost is gone from this branch
too; identical text, so the merge with p09 is clean): `AuditReader.ReadableTypesAsync` answers once per
request and `ReferencedNamesAsync` remembers every id it has looked up (found or not).
