# p04 — G1 write after write, and documented text values

Date: 2026-10-03. Piece: p04-shell (round 2). Status: accepted.

## The gap

The p04 round 1 critic planted a tenant leak in `PUT /api/identity/me/preferences`: the endpoint's
lambda captured an array, kept the previous caller in it and handed it to the next caller, in a
response header (plant P1b) or appended to `displayName` in the body (P1c). Every gate passed. Two
blind spots made that possible:

1. Tenant A's attack never sent this endpoint a body that passed validation. The switch-input
   phase built "own" bodies with `switch-<n>` in every text field, so `numerals` was refused with
   400 before the handler ran; phase 1 and phase 3 send tenant B's values. A refused write never
   reaches the code that leaks.
2. Tenant B wrote each endpoint once, before the attack, and only re-touched GETs right before
   tenant A's requests. Any state one write leaves for the next writer was never set by B right
   before A wrote.

## Decision

- **Write after write (phase 1c of `G1HttpIsolationTests`).** For every endpoint that changes
  data (except signing in and out), tenant B makes a valid write on its own records immediately
  before each valid write tenant A makes on its own records, with A's administrator cookie and
  then its bearer token; then B writes again (judged for A's markers), A reads every GET, A writes
  once more and B reads every GET. Tenant A's side is a `TenantActivity` of its own, so it builds
  bodies the same way B does (edit-and-save of the record's own GET, documented values, its own
  ids) and its answers, body and every header, are judged for tenant B's markers.
- **Both sides must succeed.** Tenant A's own writes in this phase must all answer 2xx
  (`AttackerUnsuccessfulWrites` must be empty), as tenant B's already had to. A write that the
  gate cannot make succeed is reported, not silently skipped. Ratchet: `g1.writePairs`,
  `g1.writePairEndpoints`.
- **Tenant B also writes right before and after each of A's attacks on a write endpoint**
  (`TenantActivity.TouchAsync`, phases 1 and 1b), not only reads.
- **B's own writes are told apart from the attack's.** The check "no tenant B row changed" used to
  compare one snapshot before the attack with one after; B now writes during the attack, so each
  of B's writes is framed by snapshots and only changes between B's own writes count against the
  attack (`TenantActivity.TrackChangesFrom` / `ChangedByOthersAsync`). The phases that write run in
  turn, never concurrently with B's writes.
- **Deletes are repeatable.** A delete by the activity first creates a fresh record through the
  collection's POST and deletes that one, so a delete never removes a record another write targets.
- **Documented allowed values.** `AllowedTextValuesAttribute` (kernel, `Erp.Kernel.Http`) publishes
  a text field's allowed values as its OpenAPI `enum` (null kept for optional fields). The
  preferences request (language, numerals) and the user requests (language) use it. The gate's own
  bodies take documented values (`useDocumentedValues`), now also in the switch-input phase. A shell
  gate (`Language_and_digit_fields_document_their_allowed_values`) fails if a request field named
  `language` or `numerals` stops listing exactly the supported values.
- **Plants kept as self-tests.** `LeakyModule` gains `PUT /api/leaky/me/theme` (P1b: previous
  writer's e-mail in `X-Erp-Previous-Editor`, body correct) and `PUT /api/leaky/me/density` (P1c:
  appended to the display name). Both accept only their documented values. `GateSelfTests` requires
  the attack to report each in both directions and the process-state inventory to report both
  captured arrays (`EndpointClosures`, added by p00 round 3).

- **Arrays are never immutable** in the process-state inventory (`G1ProcessStateTests`,
  `IsImmutableCore`). An array of a product type (`SessionUser?[]`) took its namespace from the
  element and was judged by the element's fields, so the captured array of the real plant was
  taken as immutable and the static check passed it. Verified on a planted copy: the HTTP attack
  reports the leak in both directions (write after write and the switch-input phase), and the
  static check now reports `closure …ProfileEndpoints.Map.previous`. The self-test plant for P1c
  has the exact shape (a synchronous lambda passing a captured array of a product record to a
  static handler).

## Why

- A plant that leaks only on the success path of a validated write is the general shape of every
  later write endpoint (approvals, numbering, imports). Requiring that the attacker's own valid
  writes succeed closes the class, not just this endpoint: a new endpoint whose valid body the gate
  cannot build fails loudly until its fields are documented.
- Documenting allowed values in OpenAPI is also what API clients need (p15).

## Cost

About two extra requests per write endpoint per touch, ~20 pairs, and two tenant snapshots per
tenant B write (a 25-user gate tenant: milliseconds each).
