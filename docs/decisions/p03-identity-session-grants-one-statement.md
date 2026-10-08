# p03 — the session's user and grants in one statement

Date: 2026-10-08. Piece: p03-identity (round 5). Status: accepted.

## Context

Round 4 added roles held in one company (decision p03-identity-roles-per-company). The session
lookup, which runs on every signed-in request, then read the user (EF Core), the workspace-wide
roles (EF Core) and the roles per company (EF Core): three statements where there had been two.
The round could not be integrated: `./erp verify` used 9,708 processor seconds against the
ratchet's maximum of 9,000 (`verify.cpuSeconds`), and the maximum may not be raised (rule 9).

Measured on the G1 company attack (about 34,000 signed-in requests), side by side with the
integration branch at `eeb8a4f` on the same machine at the same time: 286.7 CPU s for the
integration branch, 334.6 for round 4 (+17%).

## Decision

`SessionGrants.ReadAsync` reads the user (e-mail, names, language, active) and every role they
hold, workspace-wide (company null) or in one company, in one statement on the request's unit of
work (`SessionGrants.Statement`, a `LEFT JOIN LATERAL` over a `UNION ALL` of the two assignment
tables), once per unit of work. The session resolver and the sign-in answer both use it. The
statement:

- runs under row-level security like every query of the request (tenant bound by the resolver
  just before), and names the tenant itself (`u.tenant_id = @tenant`, and every join on
  `tenant_id`), the second layer the EF Core tenant filter gives elsewhere;
- reads the user's own company assignments before the company scope is bound: the table's
  policy lets a session read its own rows (`ProtectCompanyTable(..., ownRowsReadable: true)`),
  as the EF query did with the company filter switched off;
- keeps only permissions that still exist in the catalogue, as before.

Reading the grants another user holds (the access checks of user and role endpoints) is
unchanged: `GrantQueries.ForUserAsync` with the company filter, and the hidden count.

## Result

Same comparison, same machine: 261.9 (integration branch) against 277.6 (this round, the one
statement alone); with the API description generated once as well
(p03-identity-openapi-generated-once), 270.7 against 222.0.

## Why not

- **Two statements, merged in EF Core (`Concat`)**: still two statements' worth of work compared
  with one raw statement, and set operations with a constant `NULL` company translate unevenly
  across providers.
- **Caching permissions across requests**: process-wide state keyed by user, invalidated on every
  role or assignment change; a missed invalidation keeps a revoked permission alive. One statement
  per request keeps the answer exact.
- **Raising `verify.cpuSeconds`**: weakens the bar (rule 9).
