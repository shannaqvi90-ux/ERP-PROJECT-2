
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
