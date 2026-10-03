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
