# p00 — G1 non-interference: answers may not depend on the other tenant's activity

Date: 2026-10-03. Piece: p00-foundation (round 4). Status: accepted.

## Context

The G1 HTTP attack judges answers for the other tenant's markers (ids, text values, canaries).
Critics p03, p04 and p05 (round 1) each planted state shared across requests — a per-id cache, a
variable captured by an endpoint lambda, a list count cache keyed by search text — and the
count-cache shape leaks nothing but a number: tenant A is answered tenant B's total. Marker search
cannot see that class.

## Decision

`NonInterference` (tests/Erp.Gates.Tests/G1) compares, for every GET the app exposes, two answers
to the same request from the same signed-in user over the same database:

1. in the shared process, right after the other tenant sent exactly the same request (so anything
   kept per request, per id, per search or per caller holds the other tenant's answer), and
2. in a **fresh app process** (a second `WebApplicationFactory` over the same database) that only
   the judged tenant has ever used.

Status and body must be identical once date-times and trace ids are normalised. A difference is
confirmed once more before it is reported; answers that change from call to call in the fresh
process are reported as unstable instead. Before comparing, both tenants make every write the app
offers in the shared process, so state left by writes is in place. Both directions run, each with
its own fresh process. Requests cover each tenant's ids and code in every route, every documented
query parameter alone (texts both tenants hold, each tenant's own texts and ids, flags, numbers),
and every grouping and sort of every registered list.

Ratchet minimums: comparisons, endpoints, and requests whose true answers differ between the
tenants (without those the check would be blind). Self-tests plant a singleton count cache keyed by
search text and a captured previous-caller number (no marker in either) and require both to be
reported, in both directions.

## Why

- A fresh process is a reference answer that needs no knowledge of the endpoint: whatever the
  shared process keeps, the fresh one has only the judged tenant's own history.
- It complements p05's list aggregate check (totals against an exhaustive keyset walk) and the
  marker attack; it covers every GET, not only lists, and catches status codes and numbers.

## Limits

Both hosts run in one test process, so static fields are shared between them; statics are covered
by the process-state gate (every static field must be reviewed or immutable).
