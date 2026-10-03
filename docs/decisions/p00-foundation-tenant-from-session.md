# p00 — The tenant comes only from the session: guard, trace, switch inputs, source review

Date: 2026-10-03. Piece: p00-foundation (round 3). Status: accepted.

## Context

Round 2's critic planted an endpoint that read an `X-Erp-Workspace` header, ran
`set_config('app.tenant_id', …)` on the request's connection and queried with
`IgnoreQueryFilters()`. The G1 suite passed: the attack only sent tenant B's id in six fixed header
names and three fixed body names, and nothing checked how a request's tenant got bound. p01's
round-2 critic found a second blind spot: state captured by an endpoint lambda and returned in a
response header (the attack judged only bodies and `Location`; the process-state inventory skipped
compiler-generated closure classes).

## Decision

Four independent layers, each enough to catch that plant on its own:

1. **Product guard.** `ErpDbSession.BeginAsync` refuses to bind any tenant other than the
   signed-in principal's inside a request to a permissioned endpoint
   (`CrossTenantBindException`, answered 404 and logged as an error). The session lookup, sign-in,
   seeding and operator commands have no principal and bind what they resolved themselves.
   `ErpDbSession` takes the request through an optional `IHttpContextAccessor` (constructor stays
   backward compatible).
2. **Runtime trace (G1 HTTP attack).** Every binding is published as an activity on the
   `Erp.Kernel.Session` source (free when nobody listens). The gate's `SqlTrace` checks each one
   made inside a request against that request's principal; only endpoints reviewed in
   `tests/Gates/tenant-binding-endpoints.txt` (today: sign-in) may bind another tenant. It also
   records every statement sent inside a request that changes a session setting (`set_config`,
   `SET`, `RESET`, `DISCARD`) with the code that sent it (stack, or a kernel session activity as
   parent): only `ErpDbSession` may. A unit of work built outside dependency injection is caught by
   the trace even though the guard cannot see it. The trace must see the session lookup's binding,
   a sign-in binding, a kernel setting statement and a read-only transaction, or it reports itself
   blind.
3. **Deterministic switch inputs.** The test host records every header, query and cookie name the
   running app reads (`RequestInputRecorder`, wrapping the request features; installed in every
   test host, observes only). After the route phase, tenant B's id and then its code go into each
   of those inputs, one per request, on every endpoint with the attacker's own route ids, plus
   every `X-…` header literal and header/query/cookie read in `src/`, the usual guesses, and every
   uuid body field and guessed body name at once. No name list to guess.
4. **Source review.** `tests/Gates/tenant-bypass-sources.txt` lists, per rule and file, the code
   allowed to name the tenant settings, call `set_config`, send `SET`/`RESET`/`DISCARD`, switch off
   the tenant query filter (`IgnoreQueryFilters()` or the `tenant` named filter; other named
   filters are fine), run raw SQL through EF Core, build an `ErpDbSession`, call `BeginAsync`, open
   an unbound connection, take the `NpgsqlDataSource` or open its own connection. Unreviewed uses
   and stale entries fail.

Also:

- **Every response header is judged**, like the body, in both directions (attacker's responses
  for tenant B's markers, tenant B's for tenant A's).
- **Endpoint closures are process state.** `EndpointClosures` walks each endpoint's request
  delegate through the framework's closures to the product's compiler-generated closure classes
  and objects held through a captured `this`; a captured variable written inside the lambda (found
  by reading the lambdas' IL for `stfld`/`ldflda`) or a captured mutable object is a finding, keyed
  `closure <Type>.<Method>.<variable>`, reviewed like static fields.

## Why

- A list of guessed names is always one name short; the inputs the code actually reads are
  finite and observable.
- Judging what the code does (bind a tenant, change a setting) catches every way of finding the
  tenant, including ones no attack request triggers in the same run, and the guard turns the most
  likely mistake into a refusal in production rather than a leak.
- Activities cost nothing without a listener; the stack is captured only inside requests while
  the gate listens (the G1 run went from about 2 to about 2.5 minutes).

## Rejected

- A database-only defence (refusing `set_config` to the application role) is not possible:
  `SET LOCAL` cannot be revoked, and the application legitimately chooses the tenant at sign-in.
- Forbidding all captured variables in endpoint mapping would reject harmless captures of
  immutable values; reading the IL distinguishes them.
