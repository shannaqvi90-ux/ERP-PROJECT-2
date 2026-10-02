# p00 — Tenant isolation: row-level security, database roles, session-only tenant

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Decision

- **Every tenant-owned table** has `tenant_id uuid NOT NULL`, row-level security **enabled and
  forced**, and exactly one policy `tenant_isolation` (`FOR ALL TO PUBLIC USING / WITH CHECK
  tenant_id = erp.current_tenant_id()`). Migrations call `migrationBuilder.ProtectTenantTable(...)`
  which also grants DML to the app role and attaches the audit trigger.
- `erp.current_tenant_id()` reads the **transaction-local** setting `app.tenant_id`
  (`set_config(..., true)`). Without it the function returns NULL and every policy is false
  (fail closed). Because the setting is transaction-local, a pooled connection can never carry a
  tenant into the next request.
- **Three database roles**, created by the bootstrap step:
  - `erp_owner` — owns schemas and tables, runs migrations. Not superuser, no BYPASSRLS; RLS is
    forced on its tables too.
  - `erp_app` — the only role the running app gets. LOGIN, NOSUPERUSER, NOBYPASSRLS, NOINHERIT,
    member of nothing, owns nothing, cannot create objects. The host refuses to start if its
    `ConnectionStrings:App` user is anything else.
  - `erp_auth` — NOLOGIN. Owns the only two SECURITY DEFINER functions
    (`identity.resolve_login`, `identity.resolve_session`) that must find a row before a tenant is
    known. It has column-level SELECT on just the columns those functions return, through a
    SELECT-only policy. Both are on reviewed allowlists in `tests/Gates/`.
- **The tenant comes only from the session.** The session token's hash is resolved to
  (tenant, user) by `resolve_session`; the unit of work is then bound to that tenant. No header,
  query string, route value or body field is ever read to choose a tenant.
- **Second layer:** `ModuleDbContext` applies a named EF global query filter
  (`TenantId == CurrentTenantId`) to every `ITenantOwned` entity, stamps `tenant_id` on insert,
  and throws `CrossTenantWriteException` on a cross-tenant update/delete. A command interceptor
  refuses any EF command when the unit of work has no tenant.
- **Composite keys:** every `TenantEntity` has an alternate key `(tenant_id, id)`, and foreign keys
  between tenant tables reference it, so the database itself refuses a reference to another
  tenant's row. The G1 gate checks this for every foreign key.
- After every migration the migrator re-checks the invariants (RLS enabled+forced+policy on every
  table with `tenant_id`; app role not superuser/BYPASSRLS) and refuses to continue on a violation.

## Why

- CLAUDE.md rule 1 demands database enforcement with application filters as a second layer.
- Forcing RLS (not just enabling it) closes the owner bypass; a separate app role closes the
  superuser/BYPASSRLS bypass.
- Transaction-local settings are the standard safe pattern with connection pooling.
- Sign-in by e-mail cannot know the tenant beforehand; two tiny reviewed SECURITY DEFINER lookups
  are a smaller attack surface than giving the app role any cross-tenant read.

## Rejected

- Schema-per-tenant or database-per-tenant: heavy to migrate and to report across for thousands of
  small tenants, and still needs a cross-tenant sign-in lookup.
- Tenant from subdomain or header: a client-controlled input; the G1 attack sends such headers on
  purpose and must never see them honoured.
