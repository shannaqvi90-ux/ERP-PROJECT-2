# p03 identity: roles per company wait for the company scope (p02)

Date: 2026-10-03. Piece: p03-identity. Status: accepted, follow-up open.

## Context

The piece asks for a per-user default company and for roles that can be assigned per company
(critic p03 round 1, scope gap). For a UAE group running several mainland, free-zone and offshore
entities, "accountant in the Dubai LLC, read-only in the JAFZA entity" is the restricted role that
matters most.

The integration branch has no company yet. Companies, branches, which companies a user may work
in (`user_company_access`), the working company (`user_workplaces`) and the company row-level
security layer are built by p02-tenancy in parallel. p02 also adds `ICompanyContext` in the
kernel and binds the session's company scope in the session authentication handler, the same
code path identity's permission check runs in.

## Decision

- **Default company** is p02's working company: one per user, chosen by an administrator and
  switched by the user in the top bar. Identity does not keep a second copy of it.
- **Roles per company** are built on p02 once it is integrated, not before, so the two pieces do
  not each change the kernel's session binding in incompatible ways:
  - `identity.user_roles` gains `company_id uuid NULL`; null means "in every company the user may
    work in" (what every assignment means today, so existing rows keep their meaning).
  - The unique key becomes `(tenant_id, user_id, role_id, company_id)` with nulls not distinct,
    and the G1 unique-index gate keeps requiring `tenant_id` in it.
  - The permission set of a request is the union of roles with `company_id IS NULL` or equal to
    the working company from `ICompanyContext`. A company the user has no access to can never be
    named (p02's restrictive policy refuses the row).
  - The effective-permission view groups each permission's reasons by company.
  - G2 gains: a role held only in company X grants nothing while working in company Y, in the API
    and on screen.
- Until then, every role applies in the whole workspace, which is what the screens say.

## Why not now

Adding a `company_id` without companies would be an unchecked id (no foreign key, no scope
policy), the kind of column the G1 gates exist to refuse. Building a second company scope inside
identity would collide with p02's kernel change at merge time.

## Round 3 status (2026-10-03)

p02's company scope is still not on the integration branch (no companies, no `ICompanyContext`),
so the follow-up above stays open and unchanged: the critic's scope finding is acknowledged, not
worked around with an unchecked `company_id`.

## Wave 1 integrity check (2026-10-04)

p02's company scope is now on the integration branch: companies, branches, `user_company_access`,
`user_workplaces`, the restrictive `company_scope` policy, and `ICompanyContext` bound by
`CompanyScopeBinder` in the session authentication handler. The "Round 3 status" above no longer
holds. The follow-up is unblocked and still open: `identity.user_roles` has no `company_id`, every
role still applies in the whole workspace, and identity reads no company context. It is p03's next
round, not an integrity fix (it changes the permission set of every request and needs the G2 case
named above).

## Round 4: built (2026-10-05)

Built on p02's company scope as planned, with these differences from the plan above:

- **A table of its own, not a nullable column.** `identity.user_company_roles (user_id, role_id,
  company_id NOT NULL)`. Every table with a `company_id` is a company table to the G1 company gate
  (NOT NULL, standard RESTRICTIVE `company_scope`), and a role "in every company" is better told
  apart by where it lives than by a null. `identity.user_roles` keeps meaning "every company".
  The table is company-scoped with the user's own rows readable (`ProtectCompanyTable(...,
  ownRowsReadable: true)`): the session reads its own company roles before its scope is bound.
- **Roles elsewhere.** An administrator sees only the company roles of the companies they work in.
  `users.company_role_count` (kept by a trigger running with the caller's rights, left out of the
  audit trail like p02's counters) tells them the user holds roles elsewhere: such a user is shown
  read-only (`rolesElsewhere`) and every action on them is refused
  (`identity.userBeyondOwnCompanies`), because what those roles grant cannot be seen from here.
  `IUserDirectory.GetPermissionsAsync` answers every permission of the catalogue for such a user,
  so tenancy's access rules refuse them too.
- **The session's permissions.** The kernel gains `ISessionPermissionScope` (additive): after the
  scope binders run, identity adds what the roles of the working company grant. The sign-in answer
  runs the same steps for the new session, so the first screen matches; `GET /api/auth/session`
  answers the request's own claims. The web session re-reads itself when the working company
  changes (`erp:workplace-changed`).
- **Grant rules.** Assigning or removing a role in company X needs what it grants in X (or
  everywhere); a role in every company needs it everywhere; role definitions (create, copy,
  change, delete) need what they grant everywhere, because a role may be held in any company.
  Acting on a user needs every grant they hold, in the same company or everywhere.
- **Gate.** `G2CompanyRoleTests`: for every permission of the catalogue, held only in company X,
  the session, the menu, the sign-in answer and every endpoint declaring it allow it in X and
  refuse it (403) in Y; an administrator of X alone cannot define roles, give roles everywhere or
  in Y, or act on anyone holding roles outside X; a caller who works only in X sees nothing of a
  user's roles in Y and cannot act on them. Two planted faults (all company roles counted in every
  company; a company grant covered by the same permission in any company) each fail it.
- **Screens.** The user panel lists roles in one company (company and role, add and remove by
  keyboard), and the access view names the company each permission counts in.
- **Demo.** The accountant (`accountant@…`) holds "Staff" (switch company, own profile) everywhere,
  "Company manager" in the first company and "Read-only" in the second.
- **Printed and exported.** The "Users by role" report (p06's report framework) lists every
  holding: a role held everywhere leaves "Only in company" empty, a role held in one company names
  it (code and legal name in the report's language). The rows are read through row-level
  security, so a caller sees the same company roles as on the user's record; a company the
  directory does not name prints as its id rather than empty, so it is never read as "every
  company". The report's own query is three plain queries joined by UNION ALL (EF Core cannot
  translate a lateral join over a union).
