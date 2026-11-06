
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

## Round 4, fixer 4 verify (2026-11-06/07)

ERP_PROJECT=b-p12-jobs, slot 1 of 2 taken 2026-11-06T20:38:32Z, run in a quiet machine (load 1.2 at the start, highest 31). No code change in this fixer.
Every test passed: dotnet 982, web 691, e2e 143, compare 488, build 13; the G1 test that aborted in fixer 3's run (at load 54 to 69) passed. The only ratchet failure
was verify.cpuSeconds: 16,295 s (dotnet 13,413, web 2,280, e2e 542, timing 60) against 16,000 s, 295 s over, inside the 500 s allowance of owner decision #34
for a re-integration. This was not an alone run (other work used 67 % of the machine's processor time); the integrator's alone run decides. Wall time 10,290 s.
