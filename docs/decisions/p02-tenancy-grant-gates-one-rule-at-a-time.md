# p02 — Grant and scope gates that test one rule at a time, and a branch-layer attack

Date: 2026-10-05. Piece: p02-tenancy. Status: accepted (round 4).

## Context

The round 3 critic planted six faults in the tenancy rules and screens. Three of them passed every
G1 and G2 gate and were caught only by hand-written tests in `Erp.Modules.Tenancy.Tests`:

- **P3**: the "user holds permissions the caller lacks" refusal removed. The grant gate's only
  clerk worked in company X alone, so the separate "user works in companies the caller does not"
  rule refused first and hid the missing check. On a planted host a clerk who worked in every
  company removed the tenant Administrator from a company (round 2's takeover, back).
- **C4**: the "needs every company" refusal removed from company create. The company attack's
  write oracle took the first three values of company Y in each column across all company tables;
  those were branch codes (`tenancy.branches` is read before `tenancy.companies`), so company Y's
  own code never reached the company create, and its 409 went unseen.
- **C3**: tenancy's branch filter switched off. No gate attacked branch scope at all.

Two more (U1, U2: logo controls and the branch line offered without their permissions) passed
because the tenancy screens had no permission gate.

A gate that passes with one of its rules removed does not guard that rule. Hand-written module
tests guard only the endpoints someone remembered; the next module's company- and branch-owned
tables need the gates.

## Decision

1. **Every refusal has an attacker for whom only that rule can apply.** `G2CompanyAccessGrantTests`
   now also tries the clerk (holding only the endpoint's permission and reading access) working in
   every company and every branch: the company and branch rules cannot refuse, so only the
   permission rule stands between the clerk and the Administrator. The other cases were checked
   for the same property and say so: the one-company administrator holds the same permissions as
   the Administrator (only the company rule can refuse); the one-branch administrator acts on users
   without roles in company X alone (only the branch rule can refuse); the self cases change nothing
   but the caller. Every user of every case is made before any attack, so a breach early on is
   reported as that case's problem instead of breaking the set-up of the next case. A self-test
   plant (`PUT /api/leaky/company-access-partly-checked/{userId}`, every rule but the permission
   rule) must be caught by the new case and, as proof of why the gate once passed, not by the old.
2. **The company write oracle sends every identifying column of every company table.** Up to three
   of company Y's values per table (not per column across tables). A value goes first to the writes
   of its own collection (company Y's code to the company create, before a branch create can store
   it in company X), and is sent only while no row of the tenant but the victim's holds it, checked
   in the database right before each send, so a refusal can only come from the victim. The report
   lists every endpoint, field and source table it sent (`WriteOracleSources`), and the gate requires
   `POST /api/tenancy/companies [code] <- tenancy.companies` and
   `POST /api/tenancy/branches [code] <- tenancy.branches`, so the blind spot cannot come back
   silently.
3. **A branch-layer attack.** `G1BranchScopeAttackTests` runs the company attack one level down: an
   administrator limited to the first branch of company X, and the read-only user (limited to
   every branch of X but its last), attack a branch Z of X that neither may work in. The victim is
   Z's own row and every row of every tenant table that carries `branch_id = Z`, found from the
   catalogue, so a later module's branch-owned table is attacked without anyone listing it. Z's ids
   go into every route, query and body id field, its texts into every text field, valid bodies name
   company X and branch Z, the write oracle runs on every identifying field, and giving, switching
   to and opening Z are tried directly. Plants: `GET /api/leaky/branch-names` (reads branches with
   row-level security alone) and `PUT /api/leaky/branch-names/{id}` (renames any branch of the
   caller's companies) must be caught. New ratchets `g1.branchEndpointsAttacked`,
   `g1.branchAttackRequests`, `g1.branchMarkers`.
4. **A permission gate for the tenancy screens** (`web/src/modules/tenancy/screens.test.tsx`), the
   identity screens' pattern applied to companies, branches, company access and the workspace: for
   every screen, with a record open, every tenancy permission is taken away in turn and exactly
   the controls that need it disappear (buttons, file pickers, editable fields, forms); the
   new-record key opens nothing without the create permission; access the server marks read-only
   and a company where the user holds only some branches offer no change.
   `web/scripts/tenancy-plant-self-test.mjs` plants twelve faults (U1, U2 and their siblings) in a
   copy of the sources and requires the gate to fail on each; `./erp verify` runs it. The gate
   found a real fault on its first run: the company form's address fields stayed editable for a
   user without `tenancy.companies.update` (the fieldset was never disabled); fixed.

Each of the critic's product plants was re-applied to a copy of the product and run against the
gates that must catch it (P3: the grant gate; C3: the branch attack; C4: the company attack):
each gate fails with a message naming the fault.

## Alternatives considered

- **Planting the product's own faults inside the self-tests** (a test-only switch in the product
  that turns a rule off). Refused: a switch that disables an access rule is a backdoor however it
  is guarded. The self-tests plant the same faults in the test-only leaky module instead, and the
  product plants are run by hand (and by critics) against copies.
- **Branch scope in row-level security**: still refused for the reason in
  `p02-tenancy-access-as-grant.md` (a module-written session setting outside the kernel's binder).
  The branch attack now proves the application filter holds, endpoint by endpoint.
