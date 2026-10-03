# p00 — A read permission never changes data

Date: 2026-10-03. Piece: p00-foundation (round 3). Status: accepted.

## Context

Round 2's critic planted `POST /api/identity/users/{id}/reactivate` declaring
`identity.users.read`. Every G2 test passed: the exactly-one-permission matrix checks that an
endpoint opens for the permission it declares, so it trusts the declaration itself.

## Decision

- **Reads run read-only.** `ErpDbSession.BeginAsync` makes the transaction read-only
  (`SET TRANSACTION READ ONLY`) when the request is a GET or HEAD, or the endpoint is marked
  `.ReadOnlyOperation()` (for a search whose criteria travel in a body). PostgreSQL then refuses
  any write in that request, whatever the handler does. Safe methods are exactly the ones the CSRF
  defence lets through without its header, so this also closes cross-site writes through a GET.
- **Writes declare a write.** The G2 gate fails any POST, PUT, PATCH or DELETE that declares a
  read action (`read`, `view`, `list`, `search`) unless it is marked read-only or reviewed in
  `tests/Gates/read-permission-writes.txt` with the reason a reader may make the change (for
  example saving one's own view of a list one may read). Stale entries fail.
- **The trace checks the first rule end to end.** During the G1 attack every binding inside a
  read-only request must be followed by its read-only transaction; a unit of work built outside
  dependency injection inside a GET is reported.

## Why

- The declaration is the one thing the permission matrix cannot check; a rule on the declaration
  plus a database-enforced read-only transaction make the plant impossible to ship either way.
- A gate with a reviewed list (rather than a start-up refusal) lets modules built in parallel keep
  legitimate reader-owned writes, each with a written reason.
