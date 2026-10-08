# p06 — The API description built once: p03's version kept, with a gate test

Date: 2026-10-08. Piece: p06-form-report. Status: accepted.

Round 3 measured the G1 HTTP isolation attack per endpoint (scratch copy): 1,066 parameter attacks
on the anonymous `GET /api/openapi/v1.json` took 329 s, more than any other route and more than all
report endpoints together, because `MapOpenApi` rebuilt the whole document on every call, and
`./erp verify` went over its 9,000 processor-second maximum (9,310 s). This branch built the same
fix as p03 did in parallel (`OpenApiDocumentCache`, decision p03-identity-openapi-generated-once);
at the merge p03's version was kept and this branch's dropped.

What remains from this piece is a gate test (`OpenApiGateTests`): the served text equals the
framework's document serialized the same way, is the same with another Host header and on every
call (a cached description must not be stale, and must not carry a caller's Host).

Learned on the way: building the document in a hosted service while the host starts missed
endpoints (the OpenAPI and list contract gates failed), because the framework's endpoint
description is read once and kept; it must be built on the first call, as p03's version does.
