# p04 — The API description is generated once per process

Date: 2026-10-08. Piece: p04-shell, round 5 (shared kernel change, additive). Status: accepted.

## Problem

`GET /api/openapi/v1.json` was served by the framework's `MapOpenApi`, which builds the whole
document (every route, every JSON schema) on every request. Measured in the gate environment: about
160 ms of processor time per request against 11 to 18 ms for an ordinary API request, on an
endpoint anyone may call without signing in. A CPU profile of the G1 HTTP attack showed the
document's schema generation as the main producer of run-time generated code in the process, with
the finalizer thread busy destroying it. The isolation gates send this endpoint every request they
send any endpoint (every attacker, variant and read round), so it was also a large part of the
processor time the verify budget (`verify.cpuSeconds`, maximum 9,000 s) measures.

The framework also wrote the request's own address into the document's `servers` list
(`http://localhost/` in the tests), taken from the Host header: the description repeated whatever a
caller sent and differed from caller to caller.

## Decision

1. A document transformer clears `servers`. OpenAPI then means the default server `/`: clients
   resolve the paths against the address they fetched the description from, which is right behind
   any proxy. The description depends only on the code.
2. `ApiDescriptionDocument` (kernel, singleton) builds the document once through the framework's own
   `IOpenApiDocumentProvider` and serializes it as OpenAPI 3.1 JSON, the same format and content
   `MapOpenApi` produced (checked: structurally equal apart from `servers`). The endpoint returns that
   text with `application/json; charset=utf-8`, anonymous with the same reviewed reason, excluded
   from the description as before.
3. The text is a `readonly string` built in the constructor, so it is immutable process state: the
   G1 process-state gate judges it so without a review entry, and the reachable-state fingerprint
   cannot change under traffic (the gate resolves every singleton before it fingerprints).
4. Gate test (`OpenApiGateTests`): the document is byte for byte the same anonymous and signed in,
   with another Host, forwarded headers or Arabic, never contains those values or a server URL, and
   is the text the process holds.

## Measured

The G1 HTTP isolation test alone, run locally with the verify's runtime settings: 2,647 processor
seconds before (36 min 58 s, load about 50; that run also carried a five-minute stack-sampling
trace) and 2,291 after (30 min 30 s, load 12 to 30). The other attacks (the HTTP self-test on the
planted module, the company and branch attacks, non-interference) send the endpoint too.

## Alternatives considered

- *ASP.NET Core output caching on the endpoint.* Its default policy skips signed-in callers, so the
  gates (signed in) would still pay; a custom policy adds a cache service the process-state gate
  must review, and a cache keyed by the Host header grows with whatever callers send.
- *Fewer gate requests to this endpoint.* Narrows a gate (CLAUDE.md rule 9). Not done.
