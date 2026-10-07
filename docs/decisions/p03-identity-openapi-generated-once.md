# p03 — the API description generated once per process

Date: 2026-10-08. Piece: p03-identity (round 5). Status: accepted.

## Context

`GET /api/openapi/v1.json` was served by ASP.NET Core's `MapOpenApi`, which builds the whole
document (every operation, schema and transformer) on every request. Measured in a gate
environment: 50 requests took 7,963 ms of processor time (about 160 ms each), against 30 ms for
50 requests of `/api/health`. A sampled profile of the G1 company attack put the document
generation at about two thirds of the processor time spent authenticating every signed-in
request of the attack.

The isolation gates attack every endpoint, anonymous ones included, so the document is asked for
hundreds of times per attack, in each of the attacks and their self-tests. Round 4 of p03 could
not be integrated because `./erp verify` went over its processor-time maximum
(`verify.cpuSeconds`, 9,708 against 9,000), which may not be raised (rule 9). In production the
cost is the same per request: anyone (the endpoint is anonymous by review) could make the server
spend 160 ms of processor time per request.

## Decision

`OpenApiDocumentCache` (kernel, singleton) generates the document once, the first time it is
asked for, through the framework's `IOpenApiDocumentProvider` (no request), serializes it with the
OpenAPI version the options name (as `MapOpenApi` does) and keeps the UTF-8 bytes. The endpoint
`GET /api/openapi/v1.json` (same route, same reviewed anonymous reason, still excluded from the
description) writes those bytes.

- The document is a function of the code alone: routes, request and response types, permissions
  (`x-erp-permission`), anonymous reasons (`x-erp-anonymous`), examples and decimal formats. No
  transformer reads the request or a service with request or tenant state.
- Generated without a request, it names no server (`MapOpenApi` named the request's own scheme
  and host). Nothing in the product, the screens or the comparison harness reads `servers`.
- The cached bytes are process-wide state and are reviewed in
  `tests/Gates/process-state-allowlist.txt` (`singleton Erp.Kernel.Hosting.OpenApiDocumentCache._json`).
  The G1 HTTP attack fingerprints reachable state before and after the attack; the attack loads
  the document before its first fingerprint, so the cache is filled by then.

Measured: 50 requests in a gate environment, 128 ms of processor time (was 7,963 ms). The G1
company attack, side by side with the integration branch: 270.7 CPU s (integration branch)
against 222.0 (this round, with p03-identity-session-grants-one-statement).

## Why not

- **ASP.NET Core output caching on the endpoint**: needs the output-cache middleware on every
  request, and its default policy does not cache authenticated requests, which most of the
  attacks' requests to this endpoint are.
- **Caching the response per host name**: the document would still carry whatever `Host` a
  caller sent, and the cache would need a bound against callers inventing host names.
- **Generating the document at start-up**: every test environment and every `./erp up` would pay
  for it whether or not anyone asks; once on first use costs the same for those that do.
- **Fewer requests to the endpoint in the gates**: rule 9.
