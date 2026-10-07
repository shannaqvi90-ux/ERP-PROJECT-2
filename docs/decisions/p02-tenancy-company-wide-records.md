# p02 — Records every branch of a company shares are written only by someone who works in every branch

Date: 2026-10-07. Piece: p02-tenancy. Status: accepted.

## Context

Critic p02 round 4 removed the "needs every branch" refusal from `PUT /api/tenancy/companies/{id}`
(plant P7). An administrator limited to one warehouse could then rename the legal entity, change
its TRN and trade licence, or deactivate it for every branch, and every G1/G2 gate stayed green:
the branch attack treated only the other branch's own rows as the victim. The screens also
offered a one-branch administrator the company's branch line, New on the Branches screen and an
editable branch code, which the server refuses.

## Decision

- Kernel marker `ICompanyWide : ICompanyOwned` for rows every branch of a company shares (the
  company record now; later a company's settings, chart of accounts, price lists). The company
  scope binder records, once per request, the companies where the user holds only some branches
  (`ErpDbSession.SetBranchLimits`, a second call throws). `ModuleDbContext` refuses any insert,
  update or delete of an `ICompanyWide` row in such a company (`CrossBranchWriteException`,
  403 `companyNeedsEveryBranch`, logged as an error).
- That guard is the second layer, never the only one. Endpoints check first and answer with their
  own reason (`tenancy.companyNeedsEveryBranch`, `tenancy.branchNeedsEveryBranch`). Tenancy's own
  context also refuses adding or deleting a branch, or changing a branch code, in such a company.
- G1 branch attack, shared records: every table of an `ICompanyWide` entity in any module (found
  from the running app's models) is fingerprinted for company X. Every write endpoint with id
  route parameters is sent company X's id, one changed field at a time, from the endpoint's own
  read. The tenant administrator (every branch) proves each write first and undoes it. The
  one-branch administrator then sends every proven write. A success fails the gate. A refusal
  that only the kernel guard gave (its problem code) fails it too. Any change to X's fingerprint
  fails it. P7 (endpoint check off) and P7b (endpoint and kernel guard off) were both planted and
  caught. The leaky module's bug 18 keeps the self-test honest.
- Company list rows and branch records carry `everyBranch`. Screens hide the company's form and
  logo, its branch line, New branch (offered only when an active company where the user holds
  every branch exists) and the branch code where it is false. The tenancy screens gate compares
  the controls with and without it, and the plant self-test plants each of the three.

## Why

A marker on the entity, rather than a list of endpoints, means a later module's company-wide
table is guarded and attacked without anyone remembering to list it. Requiring the endpoint to
refuse first keeps the error message specific and stops the kernel guard from becoming the only
check, as CLAUDE.md rule 1 asks of the application layer.
