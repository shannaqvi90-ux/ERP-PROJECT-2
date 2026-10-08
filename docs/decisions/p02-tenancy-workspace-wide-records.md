# p02 — The workspace record is shared by every company

Date: 2026-10-08. Piece: p02-tenancy. Status: accepted (round 7).

## Context

The round 6 critic found that an administrator limited to one company, and one limited to one
branch, could rename the whole workspace and change its default language, time zone and week start
for every company (`PUT /api/tenancy/tenant` answered 200, and the Workspace screen offered Save).
This is round 4's company-wide record problem one level up: creating a company already needs every
company, but the workspace itself, which every company shares, did not. No gate looked at records
every company shares.

## Decision

- **Kernel.** A new marker, `IWorkspaceWide` (next to `ICompanyWide`), for rows every company of a
  workspace shares. `ErpDbSession` gains `SetWorkspaceHolder(bool)` (once per unit of work, like
  `SetBranchLimits`) and `HoldsWholeWorkspace` (always true for system work), exposed through a
  separate `IWorkspaceScope` interface so `ICompanyContext` stays as it is for its other
  implementers. `ModuleDbContext` refuses any insert, update or delete of an `IWorkspaceWide` row
  unless the unit of work holds the whole workspace (`CrossBranchWriteException` with code
  `workspaceNeedsEveryCompany`, 403, English and Arabic message). This is the second layer.
- **Binding.** Tenancy's company scope binder reads the workspace's company count in the same round
  trip as the rest of the binding and records `access.Count >= companyCount && every access row is
  all-branches`.
- **Endpoint.** `PUT /api/tenancy/tenant` refuses, after validation, with
  `tenancy.workspaceNeedsEveryCompany` (403) unless the caller holds the whole workspace.
  `GET /api/tenancy/tenant` answers `everyCompany`, and the Workspace screen shows the settings form
  only when it is true, otherwise a read-only note with the reason.
- **Gate.** `SharedCompanyRecords` now also fingerprints every `IWorkspaceWide` table (by tenant)
  and builds writes from every PUT/PATCH without route parameters that has a read at the same
  address, one field at a time, proven by the tenant administrator (proven only when a
  workspace-wide table changes, so a user's own workplace or preferences are not attacked). The
  company attack (an administrator of company X with every branch) and the branch attack (an
  administrator of one branch) both send every proven write; each must be refused by the endpoint
  itself (a refusal only by the kernel's guard is reported as "refused only by the last layer"),
  and the rows must not change. A leaky-module plant (bug 63, a workspace rename with SQL of its own)
  proves the check in both self-tests.

## Why

The rule mirrors company-wide records: a record shared by a set of branches or companies is changed
only by someone who works in all of them. Marking the entity rather than listing tables means a
later module's workspace-wide settings are guarded and attacked without anyone naming them.
