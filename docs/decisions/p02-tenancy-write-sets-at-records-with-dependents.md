# p02-tenancy: write sets judged at records with dependents (G2)

Round 15. Critic p02 round 14 (verdict BLOCKED, biggest gap; gate backlog row 6).

## Problem

`G2WriteSetTests` (see `p02-tenancy-permission-write-sets.md`) sent every request that edits,
deletes or acts on a record at a record the administrator had just made. A new record has no
dependent rows: a new company has no branches, nobody works in it and nobody holds a role there; a
new role is held by nobody; a new user holds no role and has no starting company. A write that
runs only when such rows exist never happened while the gate watched. The critic planted PB3 in
`CompanyEndpoints.Update`: closing a company also closed every open branch of it. A caller holding
`tenancy.companies.update` but not `tenancy.branches.update` closed every branch of the company,
and all 187 gates passed.

## Decision

1. **Every request that acts on a record is sent twice.** The first time it goes to a fresh record,
   as before. The second time it goes to a record the administrator first gave dependents, through
   the product's own endpoints (`tests/Erp.Gates.Tests/G2/WriteSetDependents.cs`). Variants sent the
   second time carry `, on a record with dependents` in their name. The dependents are:
   - **Children by reference.** For every create whose body names the record by a field called after
     it, at any depth (`companyId` for a company, `roleId` or `roleIds` for a role), the
     administrator makes one child. Other ids in the same list item are filled from the run (the
     working company, and a role that grants nothing). So a company gets a branch and a user holding
     a role in that company, and a role gets a user holding it in every company and in one company.
     This rule is generic: a later module whose create names a company, a branch, a role or its own
     parent this way gets children without any change to the gate.
   - **The record's own lists of references.** Through the record's own edit, each list on the
     record is filled with one item. A user then holds a role in every company and one in a company.
   - **Who works where.** One bystander user per run holds only the workplace switch. For a company,
     the bystander gets the company limited to the new branch and switches to work there. For a
     branch, the same happens with the branch and its company. For a user, the administrator sets
     the company the user starts in.
   - If the gate cannot make a dependent, that is a finding. It is never skipped.
2. **The second sending must also pass.** If an endpoint is accepted at a fresh record but no
   request at a record with dependents passes, that is a finding, unless it is reviewed in
   `tests/Gates/write-set-unexercised.txt` as `METHOD /route [with dependents]` with the reason.
   None is reviewed today. 17 endpoints pass at records with dependents, and the test requires the
   key ones by name: the company, branch, user and role edits, the user and role deletes, and the
   access change.
3. **Lists of references are sent emptied and with one item.** A body copies the record's own ids,
   so an edit never gave or took a role, and what that writes went unjudged. Now every top-level
   list whose items are ids the run knows (`roleIds`, and `companyRoles` with its `roleId` and
   `companyId`) is sent once emptied and once holding one item.
4. **The writes this showed were reviewed.** Before this round none of them was ever judged. Each is
   a real consequence of the action, and each is now a line with its reason in
   `tests/Gates/permission-write-sets.txt`:
   - `identity.roles.delete` deletes `identity.user_roles` and `identity.user_company_roles`.
     Removing a role removes its assignments (the handler does this, and the foreign key does it in
     companies the caller does not work in).
   - `identity.users.delete` deletes the same two tables: the account goes with the roles it held.
   - `identity.users.update` inserts into and deletes from both tables: an edit gives or takes the
     user's roles, within what the caller holds.
   - `identity.users.create` inserts into both tables: the roles a new user starts with.
   - `tenancy.access.update` deletes from `tenancy.user_workplaces`: taking a company from a user
     removes their starting place in it, by the foreign key from the access row.
   None of these writes touches a company record, a branch or another tenant. The reviewed-file
   test still holds: only company permissions write `tenancy.companies`, and the `*` permission
   writes nothing in tenancy.
5. **Closing a company leaves its branches as they are.** This is the lead's routing for this round:
   what closing a company does to its branches is a business choice, so this round keeps today's
   behaviour and adds no cascade. If a cascade is ever added, it has to be a reviewed write of
   `tenancy.companies.update` on `tenancy.branches`, and the gate will demand that line.

## Self-tests

- `The_write_set_check_catches_closing_a_company_that_also_closes_its_branches` hosts PB3's shape
  as a planted module: `PUT /api/writeplant/companies/{companyId}` declares
  `tenancy.companies.update`, and when `isActive` is false it closes the company and every open
  branch of it in the request's own transaction. The test requires
  `[isActive turned, on a record with dependents] as a user holding exactly
  [tenancy.companies.update] wrote update tenancy.branches (…), outside the write set of
  tenancy.companies.update`. It also checks that the same request at a fresh company is not
  flagged, which shows the dependents are what catch the plant.
- The critic's own diff (`gauntlet/evidence/p02-tenancy/r14/plant-PB3-company-close-closes-branches.diff`)
  was applied to the real `CompanyEndpoints.cs` once by hand. The gate reported `PUT
  /api/tenancy/companies/{id:guid} [isActive turned, on a record with dependents] as a user holding
  exactly [tenancy.companies.update] wrote update tenancy.branches (is_active), outside the write
  set of tenancy.companies.update`. The plant was then reverted.
- `Collections_and_their_record_names_match` checks how collection names are matched to record
  names. `companyId` now finds `/api/tenancy/companies`: the old rule added an "s" and looked for
  `companys`.

## Ratchet

These minimums were raised: `g2.writeSetEndpointsExercised` from 57 to 59, `g2.writeSetWritesJudged`
from 410 to 560 (575 measured), and `g2.writeSetLines` from 28 to 37. Two minimums are new:
`g2.writeSetEndpointsWithDependents` at 17 and `g2.writeSetDependentsMade` at 150 (151 measured).

## Cost

The write-set test went from about 13 s to about 14 s of wall time when run alone (401 requests).
The added processor time is well under 30 s of the verify.

## Other routing this round

The switch-company driver (ours) keeps its one-click path once a tenant has a seventh active
company. Round 14 already did this (`cb13e133`): set-up switches to the target company and back,
so the server keeps the target among the recent companies and its button shows. This round
changes nothing there.
