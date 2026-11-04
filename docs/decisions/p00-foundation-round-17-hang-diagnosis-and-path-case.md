# p00-foundation round 17: naming the hung request, and a varied-case path gate

Date: 2026-11-04. Scope: two gate changes only, no product code.

## 1. G1 names the request that fails or times out

Decision: every request of the G1 HTTP gate, TenantActivity and ListAnswers goes through
`RequestLog.SendTimedAsync` (tests/Erp.Gates.Tests/G1/RequestLog.cs). It records label, method, path
with query (not the absolute address) and seconds. When a request throws (timeout included), the
exception is rethrown as a `TimeoutException`/`InvalidOperationException` that names the request, and
the gate's failure message lists the 20 slowest requests seen so far. The 100 s HttpClient timeout is
unchanged; there is no retry and nothing is skipped. A request past it still fails the test.
`RequestLogTests` prove the naming and the listing.

## 2. Diagnosis: which paths are slow (G1HttpIsolationTests on the integration branch, 3f044e66 + the above)

One run in the toolbox container, beside two other builders' verifies (load 14 to 67), 1.32 h, failed.
Failing request: `GET /api/reports/lists/audit.log?format=xlsx&language=ar&numerals=arab&groupBy=recordType&disposition=attachment`
(answer-shapes phase, "an answer shape tenant B asked for first"), 209 s; two more audit.log requests
failed with it (`format=pdf&language=en&numerals=arab&groupBy=recordType` 231 s, `format=xlsx&language=ar&numerals=latn&groupBy=actorKind` 212 s).
Slowest after those: `numbering.sequences?format=csv...groupBy=gapless` 34 s; audit.log pdf ar
27 to 30 s (several), 10 to 18 s for audit.log pdf and json with many shapes.
Conclusion: the cause is the audit log's print and export (p07: xlsx and pdf, every answer shape),
not p10 alone; p10's numbering.sequences csv at 34 s is the second-slowest path and also needs a look.
Both are product code owned by p07 and p10 and were not touched here.

## 3. G2 varied-case path deny (gate backlog row 13, plant P16)

ASP.NET routing ignores path case, so an authorization handler testing `path.StartsWith("/api/")`
ordinally let `/API/identity/users` through. G2 now sends, for each endpoint and each caller that must
be refused, one request with a varied-case path (`/API/...`, `/Api/...` or mixed case, rotating per
endpoint so all forms are covered across the set), and requires the same refusal as the exact-case path.
Self-test: plant P16 (the handler's ordinal test) is installed in a test module and must be caught
(`g2.pathCasePlantsCaught` 1; `g2.pathCaseDenyRequests` 200 minimum in gauntlet/ratchet.json).
Cost: one extra refused request per endpoint and refused caller (refused requests stop at the
authorization handler and cost milliseconds each); see the verify numbers in the round notes.

## 4. Logging

`RequestLog` in the product's request logging records path and query, not the absolute address
(b44bfb36), so log lines do not carry host names.

## Processor time this round adds

Gate requests: about 200 refused requests, a few seconds of processor time. Request labelling: a
stopwatch per request, negligible. The G1 failure message code runs only on failure.

## 5. Gate backlog row 3 (plant P13, Host header trust)

The same varied-case deny requests also name a rotating Host (`app`, `localhost`, `admin`,
`internal.erp.local`), no extra requests. Self-test: a handler that passes any signed-in request with
Host `app` passed 51 of 204 requests and is reported (`g2.hostTrustPlantsCaught` minimum 1).
