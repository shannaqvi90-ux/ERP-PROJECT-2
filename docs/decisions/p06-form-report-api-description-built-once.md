# p06 — The API description is built once and served as the same text

Date: 2026-10-08. Piece: p06-form-report (a shared kernel fix it needed). Status: accepted.

## Context

`GET /api/openapi/v1.json` was mapped with `MapOpenApi`, which rebuilds the whole OpenAPI document
from every endpoint's metadata on every call: about 300 ms of processor time each, on an anonymous
endpoint anyone may call again and again. Measured in the G1 HTTP isolation attack (round 3,
per-endpoint timing in a scratch copy): 1,066 parameter attacks on this one route took 329 s, more
than any other endpoint and more than all report endpoints together. The same attack runs again in
its self-test, and other gates load the document too; `./erp verify` went over its 9,000 processor
second budget (9,310 s).

## Decision

`OpenApiDescription` (kernel, `Hosting/OpenApi.cs`) is a singleton that builds the document through
the framework's `IOpenApiDocumentProvider` and keeps it as a string; the endpoint serves that text.

- Built on the first call, not while the host starts: at start-up the framework's endpoint
  description can miss endpoints, and it keeps what it read (the OpenAPI and list contract gates
  failed when the document was built in a hosted service).
- The document is the same for every caller and tenant (routes and shapes only, never data). No
  `servers` entry is written: one taken from the request's Host header would differ by caller.
- The field is a read-only string set in the constructor, so the G1 process-state gate judges the
  singleton immutable; the container builds a singleton once, under its own lock.
- Gate test (`OpenApiGateTests`): the served text equals the framework's document serialized the
  same way, is the same with another Host header and on every call. The existing coverage test
  still checks every endpoint is in it.

Not chosen: response caching middleware (a cache filled under traffic is state the process-state
gate rightly flags), and building at start-up (above).
