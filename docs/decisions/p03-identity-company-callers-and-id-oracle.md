# p03 identity: callers whose grants come from a company role; record ids in the write oracle; own rows read-only outside the scope

Round 7, after critic p03 round 6 (BLOCKED: plant Pc missed by the G2 gates, plant L3 missed by
the G1 gates for the second round; RLS finding on `user_company_roles`).

## Callers holding their grants in one company (G2)

- **What was missed.** Every takeover caller (one-user and set-based) held its grants through
  workspace-wide roles. The product's per-company rule ("a grant the caller holds in company X
  covers a role held in X, nowhere else": `UserBulkEndpoints.BeyondCallerAsync` keys coverage by
  role and company; `UserGrants.CoversAll` by company) was therefore never exercised from the
  caller's side, and plant Pc (coverage keyed by role alone) passed every gate.
- **Decision.** `CompanyCallers` (tests/Erp.Gates.Tests/G2/CompanyCallers.cs) makes a caller that
  holds the endpoint's permission, reading users and roles, and one target per module's grants
  (`GrantTargets.PerModule`) through **one role assigned in company X** and nothing
  workspace-wide; it works in X and Y and starts in X (its default company is set explicitly, so
  the order of company codes does not decide where it starts). Both takeover gates aim it at:
  - a user holding a module's grants through a role in **Y** (changed only if a grant held in X is
    taken to cover Y: plant Pc);
  - a user holding them in **every company** (changed only if what the caller holds in X is taken
    to hold everywhere);
  each must stay exactly as it was (one-user path: 403 too). The control is a user holding the
  **same role in X**, which must be changed (one per module, fresh each time, because a one-user
  request may delete it). One caller per endpoint holding every module's grants keeps the cost to
  one extra sign-in per endpoint (the verify's processor-time ceiling is not raised).
- **Self-test.** Leaky module bug 57: "all that match" activation that takes a role covered in
  one company as covered in every company (every other rule correct). The set takeover check
  must report it only for the Y targets, by search and by filter. The planted endpoints of bugs
  53, 54 and 57 are judged by one shared run (`LeakyFixture.SetTakeoverAsync`).
- Checked by planting Pc in the product (and its one-user twin in `UserGrants.CoversAll`): both
  takeover tests failed, only on the new targets; unplanted they pass.
- Ratchet: `g2.takeoverSetCompanyCallerTargets` 40, `g2.takeoverCompanyCallerTargets` 60.

## Record ids anywhere in a write (G1)

- **What was missed.** The write oracle compared tenant A's answers only for text fields (an
  e-mail, a code, a name). Plant L3 answered `identityNotTheirCompany` for another workspace's
  company inside `companyRoles[]` and `unknownIds` for an id that exists nowhere.
- **Decision.** `G1WriteOracle.RunIdsAsync` walks every uuid leaf of every non-anonymous POST,
  PUT and PATCH body at any depth (fields, lists of ids, fields of items of lists of objects) and
  names tenant B's own record of the table named like the field. Tenant B first reads every list
  it can (so a registry kept from reads fills as everyday use fills it) and sends the request
  with its own record; tenant A then sends it with tenant B's record and with an id that exists
  nowhere, every other id one of its own records (so a check of the other ids never answers
  first). The answers must match: the same status, and for a refusal the same problem once
  trace id, address and every id are blanked. Both accepted counts as the same.
- An edit without a collection to create a record in (a user's company access) is sent to an
  existing record of each tenant's own, named like the route id, never the signed-in
  administrator.
- Runs in the write oracle's existing environment (no new container). Self-test: leaky bug 58
  (a membership list telling another workspace's company from an unknown id, with a registry
  filled by a list read). Checked by applying the critic's L3 plant to the product: the new test
  fails on `POST /api/identity/users [companyRoles[].companyId]` and the PUT twin.
- Ratchet: `g1.writeOracleIdChecks` 13. This is also the line-item shape later modules will use.

## Own rows readable outside the scope are not written there (database layer)

- **Finding.** `company_scope` on own-row tables is `RESTRICTIVE FOR ALL USING (company_allowed
  OR user_id = actor) WITH CHECK (company_allowed)`. INSERT was already refused (the check
  option), but UPDATE and DELETE read rows through the USING clause, so a session could delete
  its own rows of other companies, or move one into its scope (a role held in Y becoming one held
  in X). The API refuses self-changes; the database did not.
- **Decision.** Kernel helper `KeepOwnRowsReadOnly` (additive) adds RESTRICTIVE
  `company_scope_update` (FOR UPDATE) and `company_scope_delete` (FOR DELETE) with the standard
  expression; migrations apply it to `identity.user_company_roles`, `tenancy.user_company_access`
  and `tenancy.user_branch_access`. Foreign-key cascades are unaffected (referential actions
  bypass row security).
- `tenancy.user_workplaces` keeps own-row writes: the workplace switch deletes a workplace left in
  a company the user no longer works in. It is listed with that reason in
  `tests/Gates/own-row-writes.txt`.
- Gate: G1CompanyScopeTests requires both policies on every own-row table not reviewed, and tries
  live, as the row's own user scoped to another company, to move the row into the scope and to
  delete it (no row may change; the row must stay readable). The tenant gate accepts the two
  RESTRICTIVE policy names (they only narrow); a permissive one by those names is unreviewed.
  Self-test: a table missing them, and one with a permissive delete policy.
