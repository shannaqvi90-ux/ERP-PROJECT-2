# p07-audit round 8: integration fix 1 (import-5000 health check)

The integrator's two alone verifies of the p07 r8 merge passed every dotnet, web and Playwright test (run 2) and failed only the comparison harness's health check of `import-5000` ("the page changed what it shows without a counted step causing it", a StyleRecalcInvalidationTracking 0.18 s into the measured part). p07's diff is backend only; the cause is on the integration branch.

Cause, found by reproducing it on a clean stack of this merge (2,000 users; the same failure in 5 of 8 harness runs with `--task import-5000 --health`, on a quiet machine): the Contacts list's view tabs (p18, `viewCounts`) ask one count request per tab after the list renders, 6 requests besides the list, two of them for tabs with the same filter. The list's render is a 50 ms task under the harness's tracing; the count answers wait behind it and are handled at about 0.17 s, when the harness's step 2 (the Import click) is starting, and the render that shows the counts is attributed to a harness DevTools command of that step instead of step 1.

Change (web/src/kernel/lists/ListView.tsx): tabs with the same filter share one request (7 requests became 5, within the browser's 6 connections per host), and the counts are shown in one commit (`flushSync`) when the last answer is in, instead of one render per answer. Unit test added (ListView.test.tsx, same filter asked once, every tab counted).

Result: on the same stack the failure rate fell from 5 of 8 runs to 5 of 18 runs (not zero), so this is a partial fix. It does not remove the race: any answer that is handled after the list's long render can land next to the harness's next step. Further options, for p18 (owner of the tab counts): count answers delivered with the list's own answer, or counts asked only on the person's first action on the tab strip. Not done here: both change a p18 feature.

Verification (targeted, no `./erp verify`): web unit tests 809 of 809 (76 files), tsc clean. Processor time added: none measurable.
