# p05 — G1 judges what list answers add up to, and never trusts a reviewed root for what it holds

Date: 2026-10-03. Piece: p05-list-search (round 2). Status: accepted.

## Context

Round 1's critic planted a 60-second count cache in `ListBinding.QueryAsync`, keyed by search and
filter with no tenant (plant L3). Tenant A then received tenant B's totals while every row stayed
A's own. The HTTP isolation gate passed, because it only looks for tenant B's ids and text in
responses, and the process-state gate passed, because `ModuleCatalog._modules` was reviewed as a
whole, so nothing hanging off a module registration (list bindings, menus, registration lambdas)
was ever inspected. The same leak through group counts and money totals would pass too, and money
modules are where grouping with totals will be cached.

## Decision

1. **List answers are judged against the asking tenant's own rows** (`ListAnswers`, a phase of
   `G1HttpIsolationTests`). For every registered list, the other tenant first sends exactly the same
   request (everything; one-letter searches; the other tenant's own words; every value of every
   choice and flag column; text filters; `is null` on every filterable column; every grouping alone
   and with searches), then the judged tenant sends it. Its `total` must equal the number of rows an
   exhaustive keyset walk of the same query returns; every walked row must be its own (ids read from
   the database); each group's count and totals must equal those of the walked rows with that
   value; group counts must add up to the total. Both directions run. Each list must have at least
   one query whose true answers differ between the tenants, or the check reports itself blind.
2. **Reachable state is judged field by field** (`ReachableState`, in `G1ProcessStateTests`). The
   walk starts from every product singleton instance and every static field, goes through framework
   collections, arrays, delegates (to their closures and targets), expression trees and lazies, and
   judges every field of every product object it reaches with the same rules as singleton fields. A
   registration object's fields are reviewed one by one in `tests/Gates/process-state-allowlist.txt`
   (`reachable …` lines), never through the root that holds them.
3. **State must not change under traffic.** The HTTP attack fingerprints everything reachable from
   those roots (values, collection sizes, types, by path) before tenant A attacks and again at the
   end; any difference fails the gate.
4. **List bindings are immutable.** `Column` and `InMemory` return a new binding over a frozen
   dictionary, so a registered binding has no state a request could change.
5. **Shared-view writes need the list's own permission before the handler runs** (an endpoint
   filter answering 404, as for a list the caller cannot read), in addition to the handler's own
   check. G2 tries every endpoint under `/api/lists/{list}/` as a user holding only
   `lists.views.share` and as a reader of the list without it, against real views, and then as the
   administrator (so the refusals were the permission checks, not bad requests).

The gate self-tests plant each shape: a list module whose totals and groups are remembered across
tenants (found by the list answers in both directions, and by the reachable-state walk and the
fingerprint), and a catalogue holding a binding with a count cache and a registration closure that
counts calls (found field by field).

## Consequences

- The isolation gate takes longer (each list query is walked page by page in both tenants).
- A future module that caches anything on a registration object fails the process-state gate until
  the field is made immutable, scoped to the request, keyed by tenant, or reviewed on its own line.
