# p02-tenancy: a permission counts only in the companies whose roles grant it

Round 12. Critic p02 round 11 (verdict BLOCKED, biggest gap).

## Problem

Roles can be held in one company (p03): the accountant of the seeded demo is "Company manager" in
ALN-DXB and "Read-only" in ALN-FZE. A request's permissions were the workspace-wide roles plus the
roles of the working company, while its company scope (row-level security and the company query
filter) was every company the user works in. No endpoint asked whether the permission was held in
the company of the record it touched, and the kernel's write check asked only whether that company
was in the scope. So, working in DXB, the accountant changed FZE's trade licence (`PUT
/api/tenancy/companies/{FZE}` 200), changed an FZE branch and opened a new branch in FZE (201); the
FZE form opened editable with Save. Every gate passed: `G2CompanyRoleTests` only checked a user
working in the company where the role is not held.

## Decision

The permission an endpoint declares is authorised in the company of every record the request
touches, by the kernel, for every module:

- `ISessionCompanyGrants` (kernel contract, implemented by identity's `SessionGrants` from the
  grants the session already reads in one statement, so no extra query): the companies whose roles
  grant a permission. `ICompanyPermissions` (kernel, scoped) answers "is this permission held in
  company C" (workspace-wide roles: everywhere).
- The session authentication handler, once the scope is bound and the permissions are known,
  reads the endpoint's `RequiresPermission` and calls `ErpDbSession.LimitToPermissionAsync`: null
  when the permission comes from a workspace-wide role (nothing changes), otherwise the companies
  whose roles grant it. Set once per request; it can only narrow.
  - Writes: `ModuleDbContext` refuses to add, change or delete any company-owned row
    (`ICompanyOwned`, old and new company) outside those companies: `CompanyPermissionWriteException`,
    answered 403 `permissionNotHeldInCompany` (English and Arabic message), logged as an error. The
    records every company shares (`IWorkspaceWide`) need the permission workspace-wide.
  - Reads: a read-only request (GET, HEAD, `ReadOnlyOperation`) gets its company scope narrowed
    to those companies, in the database (`app.company_ids`, so row-level security itself hides the
    rest) and in the query filter. A company the role does not reach does not exist for that read
    (404, as for an id that exists nowhere).
  - A write request keeps reading in the whole scope, so a module can name its refusal (identity
    answers 403 `identity.grantBeyondOwn` to a role given in Y by an administrator of X, as its
    gates expect) instead of "unknown id".
- Tenancy refuses early, with the same code, where the record's company is known: company change,
  logo, branch create and change, access change (any company in the body where `tenancy.access.update`
  is not held). The access change treats companies where the caller may not change access as
  "left as they are" (they are neither given nor taken), and its answer shows only companies where
  the caller reads access.
- Screens offer a change only where the role is held: `CompanyDto`/`CompanyRow` carry `canUpdate`
  and `canAddBranch`, `BranchDto`/`BranchRow` `canUpdate`, access options `canChange`. The company
  form is read-only with the reason ("Your roles let you read this company but not change it. A
  role given in another company counts only in that company.", and in Arabic), without Save, logo
  change or the branch line; the branch form likewise; New branch lists only companies where a
  branch may be added; the access form disables a company it may not change and does not send it.

Alternatives weighed:

- Narrow the company scope for every request (writes too). Rejected: identity's gates expect 403
  (not 400 "unknown ids") for a role given in Y by an administrator of X, and its account rule
  (G2AccountTakeoverTests: an administrator of X may act on a user who also works in Y and holds
  nothing beyond X's grants there) counts the companies of the scope. Reads are narrowed; writes are
  refused by the kernel check.
- A second database layer for the write check (a restrictive policy per company table, or a check in
  the audit trigger). Not taken this round: the kernel check sits under every EF write of every
  module, raw statements in request paths write only tenant-wide counters, and the new gate attacks
  every endpoint through HTTP (its self-test plants a raw statement past the kernel check and is
  caught). Recorded here so a later piece can add it.
- User accounts are records of the whole workspace and stay under identity's own rule (the caller
  holds, in each company, what the user holds there; the user works nowhere outside the caller's
  scope). Narrowing "outside the scope" to "outside the companies where the permission is held"
  was tried and broke the reviewed control of `G2AccountTakeoverTests`, so it was reverted.

## Gates

- `G2CompanyRoleTests.A_role_held_in_company_X_acts_on_nothing_of_company_Y`
  (`tests/Erp.Gates.Tests/G2/CompanyRoleRecords.cs`): an administrator of X alone (staff permissions
  workspace-wide), who works in X and Y and works in X, sends every endpoint whose permission they
  hold only in X (99 endpoints, about 900 requests) Y's row ids in every route parameter (four rows
  of each company table), Y's id in every company field of a valid body, `companyId` query and
  filter; a user of X where a route names a user. No Y row may change (checksums of every company
  table), no answer may carry Y's ids or Y-only text, a read by Y's id must answer as one by an id
  that exists nowhere, no 5xx. Named checks: the critic's writes on Y (trade licence, logo, branch
  change, branch create, giving and limiting access in Y) answer 403; the same writes in X answer
  200/201 (controls); reading Y answers 404 and the companies list shows X alone. With the kernel
  limit switched off by hand, the gate fails ("Company Y rows changed ...: tenancy.branches,
  tenancy.companies").
- `G2CompanyRoleTests.A_manager_of_X_who_reads_Y_is_offered_changes_in_X_alone`: the flags of
  companies, company rows, branches and access options are true in X and false in Y, working in X
  or in Y; changing Y answers 403 `permissionNotHeldInCompany` working in X, `auth.forbidden`
  working in Y.
- `G2CompanyRoleTests.The_company_role_attack_catches_a_permission_checked_in_the_working_company_only`
  (`CompanyRolePlantModule`): a branch changed by id with a raw statement and Y's legal name answered
  by a writing request are both found.
- `CompanyScopeTests.A_permission_held_in_some_companies_writes_only_there_and_a_read_sees_only_them`
  (kernel, below HTTP).
- Web: company, branch and access screens read-only where the role is not held; the access save
  sends only changeable companies.
- Ratchet: `g2.companyRoleRecordEndpoints` 99, `g2.companyRoleRecordRequests` 850.

## Other round 11 findings

- Write-set gate (plant P1d): every number of a body is also sent at its largest valid value (the
  valid body sends the smallest), so a side door writing a value equal to the stored one is still
  seen; `WriteSetPlantModule` carries P1d (fiscal year start month) and the self-test requires
  `PUT /api/writeplant/workplace [fiscalYearStartMonth=12] ... wrote update tenancy.companies
  (fiscal_year_start_month)`.
- English legal name: must hold at least one Latin letter (`tenancyLegalNameEnLatin`, English and
  Arabic); "فرع ديرة 60473" and "60473" are refused, "3M Gulf 60473" accepted.
- The companies list refreshes when a branch is added from the open company's form.

## Cost

The limit is read from grants already in memory; a narrowed read adds one `set_config` statement,
only for users whose permission comes from a company role.

Processor time this round adds to `./erp verify`, measured on the host: the three new company-role
gate tests about 15 s (their fixture already exists), the write-set gate's number variants a few
seconds (the whole write-set test is 44 s), the six new screen plants about 75 s (one run of the
tenancy screen gate is about 13 s), the new unit, screen and end-to-end tests a few seconds: about
100 to 150 s in all. The branch's full verify (every test green: .NET 660, web unit 510, end to end
107, comparison 415, build 6) used 10,701 processor seconds (dotnet 8,194, web 2,188, e2e 283,
timing 36) against the 10,500 s maximum, on a busy machine (load up to 77 on 16 CPUs); critic
round 11's run of the integration product used 10,294 s (dotnet 7,913, web 2,045).
