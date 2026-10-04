# p02 — Company scope: a second row-level security layer inside the tenant

Date: 2026-10-03. Piece: p02-tenancy. Status: accepted.

## Decision

- A tenant (customer workspace) holds one or more **companies** (legal entities). A row that
  belongs to a company carries `company_id uuid NOT NULL`. Migrations call
  `migrationBuilder.ProtectCompanyTable(schema, table)` after `ProtectTenantTable`. It adds one
  **RESTRICTIVE** policy `company_scope FOR ALL TO PUBLIC USING / WITH CHECK
  erp.company_allowed(company_id)`. Restrictive policies are AND-ed with `tenant_isolation`, so
  they can only narrow what a tenant sees, never widen it.
- `erp.company_allowed()` reads two transaction-local settings, `app.company_scope` (`all`,
  `list` or anything else, which means nothing) and `app.company_ids`. They count only together
  with `app.company_tx`, the transaction's start time, which works like `app.tenant_tx`. Missing
  or stale settings fail closed.
- `ErpDbSession.BeginAsync` starts every unit of work with a company scope. System work (seed,
  job, system, operator) gets `all`. A signed-in user gets `none` until the session is bound.
- **Binding the session.** The kernel's session authentication handler calls every registered
  `ISessionScopeBinder` once the token resolves. The tenancy module's `CompanyScopeBinder` reads
  the user's own access rows and calls `ErpDbSession.BindCompaniesAsync(ids)`. That call works
  once per unit of work, so code later in the request cannot widen the scope. The binder also
  records the working company and branch (`SetWorkplace`) and the branches the user may work in.
  All of this is exposed to modules as `ICompanyContext`.
- **Own rows before the scope exists.** The access tables (`user_company_access`,
  `user_branch_access`, `user_workplaces`) use the variant `USING (erp.company_allowed(company_id)
  OR user_id = erp.current_actor_id())` with the standard `WITH CHECK`. The session can read its
  own access rows to build the scope, but it can never write them outside the scope.
- **A new company.** The `companies` table is itself company-scoped (`company_id = id`, check
  constraint). Its creator could not insert it into a scope that does not contain it yet, so the
  create endpoint calls `ErpDbSession.IncludeNewCompanyAsync(newId)` (a brand-new id only). It
  then writes the company and the creator's access to it in the same transaction.
- **Second layer in the application.** Entities implementing `ICompanyOwned` get a named EF
  query filter `company` (`AllCompanies || CompanyIds.Contains(CompanyId)`). Writes to a company
  outside the scope throw `CrossCompanyWriteException`, which is answered as 404 like a
  cross-tenant write.
- **Which companies.** The scope is every company the user has access to, active or not, so
  administrators can reactivate a company. The switcher offers only active companies. The
  working company is the default for new records and lists. It is context, not security.
- **Branches** are company data (company-scoped). Branch-level limits on users are kept in
  `user_branch_access`. They are enforced by the workplace switcher and exposed as
  `ICompanyContext.BranchIds` for later modules. Since round 3, tenancy also holds its own branch
  reads and writes to them: a named query filter on `Branch`, set once per request by the scope
  binder (`TenancyBranchScope`). Creating a branch, or changing a branch code, needs every branch
  of the company. See `p02-tenancy-access-as-grant.md`. There is no branch-level row-level
  security policy yet: no table holds branch-owned business data.

## Why

The goal says "data that belongs to a company is scoped to it", and the bar demands that a user
of company X cannot reach company Y. Enforcing that only in application code would make every
future module responsible for it, with nothing to catch a missed `WHERE company_id = …`. A
restrictive policy makes the database refuse it, the same way the tenant policy does. Every later
module gets this for free by calling one migration helper. Odoo enforces multi-company with ORM
record rules only; we hold the stronger line.

## Gates (made stricter, not weaker)

- `G1CompanyScopeTests` (new): every tenant table with `company_id` has exactly the standard
  restrictive policy (or the own-rows variant on a `user_id` table). The app role sees exactly
  the bound companies' rows and nothing without a binding or from a stale binding, and cannot
  update, delete, move a row into, or insert a row into another company. Ratchet
  `g1.companyTables`.
- `G1CompanyIsolationTests` (new): an administrator who may work only in company X, and the
  read-only user of X, send company Y's ids and Y-only texts through every route, query and body
  field of every endpoint. They also try to grant Y, switch to Y and open Y. Any Y marker in an
  answer, any GET answering differently for a Y value than for a value that exists nowhere, any
  5xx or any change to Y's rows fails it. Ratchets `g1.companyEndpointsAttacked`,
  `g1.companyAttackRequests`, `g1.companyMarkers`. A planted leaky endpoint (widening the scope)
  proves the attack catches it.
- `G1DatabaseIsolationTests` binds `app.company_scope = all` when it checks tenant isolation, as
  the platform does for system work. Its structural check lets a policy named `company_scope`
  pass only if it is RESTRICTIVE. A restrictive policy cannot grant access. The company gate
  checks its exact expression, and a self-test proves that a permissive policy of that name
  still fails the tenant gate.
- Tenant B's own writes in the G1 attack (and the attacker's non-attacked fields) now conform to
  each field's documented OpenAPI constraints: enum, pattern with its documented example,
  maxLength, minimum and maximum. Without this, validated fields such as company codes would
  never reach a handler. Requests that get past validation make the attack stronger.
