# p03 identity: a user who also works in a company the caller does not work in is beyond the caller

Round 8, routed from critic p02 round 8 (the default-company scope check in `UserWorkplaces`
could be deleted with every gate green; identity's own target check counted only hidden company
roles).

## The fault

`UserAccess.RefusalAsync` (one user) and `UserBulkEndpoints.BeyondCallerAsync` (all that match)
refused a target who held a permission the caller lacks, or any role in a company the caller does
not work in. A user holding only a workspace-wide role the caller's grants cover, but who also
works in company Y, was within reach of an administrator who works in X alone. Resetting that
user's password (or changing their e-mail, ending their sessions) lets the administrator sign in
as them and work in Y: a reach beyond their own companies, the same takeover the role rules
prevent.

## Decision

- One mechanism, the tenancy module's: identity asks `IUserWorkplaces` (tenancy's public contract,
  which the default-company endpoint already uses) whether the user works elsewhere.
  `GetAsync(userId).CompaniesElsewhere` (all the user's companies, counted outside the scope in
  `user_company_totals`, against the access rows the caller's scope shows) serves one user;
  `WorkingElsewhereAsync()` (new, additive) is the same count as a set, for "all that match":
  empty at once when the caller's scope holds every company (the usual workspace administrator),
  otherwise the ids of the workspace's users whose total exceeds what the scope shows.
- Refusal code `identity.userWorksBeyondOwnCompanies` (English and Arabic), checked after hidden
  roles and before the grants. "All that match" counts such users in `refusedBeyondOwn`.
- `GET /api/identity/users/{id}` carries `refused`: the problem code any action on that account
  would answer for the caller (absent when allowed, and on the caller's own). The user panel
  offers nothing then and says why ("also works in, or holds roles in, companies you do not work
  in"), so the screen agrees with the server in cases it cannot judge from the list row.

## Gates

- `Acting_on_a_user_needs_every_permission_that_user_holds`: for every endpoint acting on a user,
  while the caller works in the first company alone, a fresh user holding only a workspace-wide
  reader role and working in both companies must answer 403 and stay unchanged.
- `SetTakeover`: the same target, by search and by filter, while the caller works in one company.
- Self-test: leaky bug 65, "all that match" with every role rule right that leaves out where users
  work; the set takeover check must report it only for that target. Bug 57 now respects where
  users work, so it keeps exactly its own fault.
- Checked by removing the workplace check from the product (both places): both takeover tests fail
  on the new target (14 problems); restored, they pass.
- `G2CompanyRoleTests`: the control of "a caller who works only in X resets the password of a
  user holding a role in X" now names a user who works only in X; the old target (who also works
  in Y) is now an expected 403. The identity module's all-that-match test gains a user without
  roles working in both companies (refused for the X-only clerk, changed by the clerk of both).

## Cost

One user creation and two requests per endpoint acting on users and per set endpoint selector, in
the existing gate environment; a few seconds. `WorkingElsewhereAsync` costs nothing for callers
who work in every company.
