# p02 — Company and branch access is a grant

Date: 2026-10-04. Piece: p02-tenancy. Status: accepted (round 3).

## Context

The round 2 critic showed that `PUT /api/tenancy/access/{userId}` checked only that the companies
named were in the caller's scope. A clerk whose role held only `tenancy.access.read` and
`tenancy.access.update` removed the tenant Administrator from a company, and the Administrator
could not undo it (nobody changes their own access). A manager limited to one branch gave someone
every branch. Branch limits affected only the switcher. An administrator of one company learned,
from a 409 on create, which company codes companies they cannot see use. No gate looked at any of
this.

## Decision

Company and branch access follows the same rule as roles: a caller gives and takes only access
they hold, and only on users who hold no more than they do.

1. **Nobody changes their own access** (as before).
2. **Permissions.** The user may hold no permission the caller lacks. Tenancy reads the user's
   permissions through a new identity contract method, `IUserDirectory.GetPermissionsAsync`
   (additive; identity owns roles). This is the same rule identity applies to editing users.
3. **Companies.** The user may work in no company outside the caller's own. The caller cannot see
   rows outside its company scope (row-level security), so the database keeps a count:
   `tenancy.user_company_totals` (one row per user, kept by an invoker-rights trigger on
   `user_company_access` inserts and deletes). The handler compares the user's total with the rows
   it can see. A larger total means the user holds more, and the caller is refused
   (`tenancy.userBeyondOwnCompanies`) without learning which companies they are.
4. **Branches.** In each company whose access changes, the caller's own access must cover the
   user's access before and after the change. Every branch covers anything. A list of branches
   covers only a subset of itself. So a one-branch manager gives only that branch, and cannot
   narrow or remove a user who works in every branch (`tenancy.grantBeyondOwn`).
5. **The screen says so before you try.** `GET /api/tenancy/access/{userId}` returns `canEdit` and
   a `readOnlyReason` text key, and marks each company option with `canGiveAllBranches`. The
   access form disables what the caller may not do and says why, in English and Arabic.

**Branch limits hold everywhere in tenancy.** A request's branch limits (companies where the user
holds only some branches, and those branches) are set once by the company scope binder in a
scoped `TenancyBranchScope`. A named query filter on `Branch` shows only those branches in such
companies. Lists, reads, updates and the access form's options all go through it, so a
one-branch user reads and edits only their own branch. Creating a branch, or changing a branch
code, needs every branch of the company. Branch codes are unique within the company, so a refusal
would otherwise name a branch the caller cannot see. The new branch would also be one they could
not work in.

**Company codes.** Codes are unique across the workspace. Creating a company, or changing a
company's code, needs every company of the workspace (`tenancy.companyNeedsEveryCompany`), so no
refusal can tell a caller about a company they cannot see. The workspace's company count is kept
by a trigger on company inserts in `tenancy.tenants.company_count`. Companies are never deleted.
The count is never returned by any endpoint, and the audit trigger on `tenants` leaves it out of
the change set.

## Alternatives considered

- **A SECURITY DEFINER lookup of a user's companies.** Refused. The ratchet allows at most two
  security-definer functions (both are identity's pre-tenant lookups), and an in-request
  cross-scope reader is exactly the class of code the G1 gates exist to keep out.
- **Branch scope in row-level security.** A second session setting written by a module, outside
  the kernel's write-once binder, would be weaker than it looks. Branch scope is a within-company
  business limit, not a tenant boundary, so the app-layer filter, set once per request, is the
  right strength. Company and tenant scope stay in row-level security.
- **Odoo's way** (only Settings administrators edit allowed companies). This would forbid
  delegation entirely. The rule above allows delegation without escalation.

## Known limit

Two administrators of the same company who hold the same permissions are peers in it. Either may
change the other's access to that company if neither works in a company the other lacks. This is
deliberate: the tenant's owner, who works in every company, cannot be touched by a one-company
administrator.

## Gates

- `G2CompanyAccessGrantTests` (with self-test plant `PUT /api/leaky/company-access/{userId}`)
  finds every endpoint whose body carries a `companies` grant field. It checks the junior,
  one-company and one-branch cases, and the caller acting on themselves.
- `tests/Gates/endpoint-permissions/tenancy.txt` reviews the permission of every `/api/tenancy`
  endpoint. A new test requires every module route to sit under a reviewed map, or, for the
  lists module, to declare its list's own permission.
- The G1 company attack sends valid bodies (plant C2 is now caught through the changed company Y
  rows). Before it writes anything of its own, it runs an in-tenant write oracle: company Y's
  codes and other identifying values against values that exist nowhere.
