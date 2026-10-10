# p03 identity: role edits judged by callers whose grants come from a company role; workspace permissions on screen

Round 8, after critic p03 round 7 (BLOCKED: plant Pf, a role edit that took grants held in ONE
company as held everywhere, passed every gate; the G3 failure of that round was the write
oracle's fixture contamination, fixed by p00 on the integration branch in 1a1e944 and b49f935).

## What was missed

`GrantBearingRecords` (the gate behind `Acting_on_a_record_that_grants_access_needs_everything_it_grants`)
judges PUT, DELETE and copy on every grant-bearing record, but every caller it made held its
grants through workspace-wide roles. The round-7 `CompanyCallers` reached only the user takeover
gates. The product's rule for a role ("roles are defined for the whole workspace and may be held
in any company, so changing one needs what it grants in every company": `RoleEndpoints.Update`,
`Delete`, `Copy` call `CoversAll(..., null)`) was never exercised from a caller holding grants in
one company, so a regression to "grants held in some company" went unseen.

## Decision (gate)

- For every family whose create request carries `permissions` (a role, or a later module's
  record that hands out permissions directly), every endpoint acting on one of its records is also
  sent by a `CompanyCallers` caller: one role in company X holding the endpoint's permission,
  reading users and roles, and one target per module's grants (`GrantTargets.PerModule`); nothing
  workspace-wide; works in X and Y, starts in X.
- For each module target, a **fresh** record granting it is held by a user **across the
  workspace** and another by a user in **company Y alone**. The request (an edit changes one text
  field; delete; copy) must answer 403 and leave the record exactly as read before. An edit is
  also sent with the last permission taken away, and a record granting nothing, held in the same
  place, is sent the target's permissions (granting more): both 403, unchanged.
- The control is a record granting nothing, held by a user in X: accepted (200, 204 or 201), so
  the 403s come from the grant check, not from a malformed request.
- Not asserted: a record held only in X, or by nobody. Whether a company-scoped role manager may
  change those is a product choice (today: refused, the strict rule); the gate pins only what is an
  escalation in every reading (changing access that people elsewhere hold).
- Families whose records are users (`roleIds`, `companyRoles`) are left to the takeover gates,
  which already aim company callers at every endpoint acting on a user.
- Self-test: leaky bug 64 (`/api/leaky/company-roles`): create checks grants held everywhere;
  change, delete and copy take grants held in any one company as held everywhere (Pf's shape).
  The check must report all three endpoints, for both places, read the rename, the deletion and
  the emptied permission back, and report nothing else of that family (workspace-wide callers and
  the control meet correct checks). It shares the leaky fixture's one grant-bearing run.
- Checked by planting Pf in the product (`RoleEndpoints.Update` judging `held.Anywhere()`): the
  gate test fails on `PUT /api/identity/roles/{id:guid}` for both places; unplanted it passes.
- Ratchet: `g2.grantBearingCompanyCallerTargets` 50 (3 role endpoints, 5 module targets, 2
  places; the edit sends 3 requests per target and place).

## Decision (screens)

The roles screen judged a role's grants against the session's permissions, which include what a
role in the working company grants: a company-scoped role manager was offered Save, Copy and
Delete on roles the server refuses. The session answer now carries `workspacePermissions` (what
the user's roles grant in every company; additive, omitted when signed out), and:

- `roleActions(role, held, everywhere)` judges what a role grants against `everywhere` (the
  action's own permission still comes from the session);
- the permission matrix offers only permissions held everywhere;
- the user editor's workspace-wide role picker offers only roles granting what is held
  everywhere (company roles keep the session's, which covers the working company).

## Cost

One more sign-in per role endpoint and about 50 requests plus 60 record and holder creations
in the gate environment that already runs the grant-bearing check; measured 22 to 48 s of that
test's wall time on this machine (it was 18 s before), well under 100 processor seconds. No new
container or environment.

## Processor time of this round's verify

Full `./erp verify` runs of this branch (all tests passing in each):

- 2026-10-10 00:16Z, merged with p01 round 9: 10,781 processor seconds (dotnet 8,798, web 1,641,
  e2e 294, timing 49) and 4,253 s of wall time, while a critic's verify held the other slot
  throughout (load 5.2 at the start, 7.5 at the end): over the 10,500 s maximum, judged.
- 2026-10-10 01:46Z, merged with p06 as well: 9,905 processor seconds (dotnet 7,878, web 1,506,
  e2e 433, timing 89), 3,119 s of wall time: passed.

The web stage rose from 252 s in this round's first run (before the p01 round 9 merge brought the
harness's instrument mutations) to about 1,500 s; that is not this piece's. This round's own gate
work (company callers on grant-bearing records, users working elsewhere, the two planted
families' six endpoints that the G1 self-test attacks too) adds an estimated 200 to 300 s.
