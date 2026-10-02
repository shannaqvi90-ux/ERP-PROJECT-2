# p00 — Cross-tenant lookups answer only on an unbound connection

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Context

Sign-in and session resolution must find an account or session before a tenant is known, so
two SECURITY DEFINER functions (`identity.resolve_login`, `identity.resolve_session`) read
across tenants. The application role may execute them. In round 1 a critic showed that any new
endpoint could call `resolve_login` inside its request and so look up another tenant's users by
e-mail.

## Decision

1. Both functions return no rows when the transaction has a tenant bound
   (`app.tenant_id` set). Every request runs inside a tenant-bound transaction once
   authenticated, so an endpoint that calls them gets nothing. Sign-in and the session resolver
   call them on the unbound connection before binding, as before. Implemented in the identity
   migration `ReviewedLookupsUnboundOnly`, replaced as `erp_auth` so ownership, grants and the
   pinned `search_path` stay as reviewed.
2. The kernel's authentication handler marks the request while it resolves the session
   (`SessionResolution.InProgressItem`), so tests can tell the authentication phase from endpoint
   code.
3. `tests/Gates/security-definer-callers.txt` records, per function, the reviewed caller
   (`endpoint:auth.signIn`, `authentication`) and the source files allowed to name it. The G1
   gate traces every statement at run time and scans `src/` statically.

## Why not a separate database role for sign-in

A second login role with its own connection pool would also stop the lookup, but adds a
credential, a pool and compose wiring for one query. Layer 1 makes misuse return nothing; layers
2 and 3 make misuse fail the build. Revisit if more cross-tenant lookups appear (the ratchet caps
SECURITY DEFINER functions at 2).
