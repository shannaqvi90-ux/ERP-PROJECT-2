# p02-tenancy: the per-company write permission is enforced by the database too

Round 14. Critic p02 round 13 (verdict BLOCKED, biggest gap; round 12's gap again).

## Problem

Round 12 made a permission count only in the companies whose roles grant it
(`p02-tenancy-permission-in-the-records-company.md`). The second layer behind each endpoint's own
check was the kernel's `ModuleDbContext.SaveChanges` guard, which sees only tracked writes. A
set-based `ExecuteUpdate` or `ExecuteDelete` (the codebase uses them) never reaches it. The critic
planted one in `BranchEndpoints.Update`: when isActive goes from true to false, close the branch in
one statement and answer, before the `PermitsCompany` check. Live, the Dubai LLC's accountant
(manager in ALN-DXB, read-only in ALN-FZE) closed a free-zone branch with 200, and all 172 gates
passed. The company-role gate (`CompanyRoleRecords`) sent Y's records only generated bodies, where
every boolean is true, plus two named text edits. So no path that runs only when one field changes
(a boolean turned off, an enum changed) was ever reached against company Y.

## Decision

1. **Database layer (product).** The per-company write permission is now a row-level security
   rule, so it holds whatever statement the code sends (tracked SaveChanges, ExecuteUpdate,
   ExecuteDelete, raw SQL):
   - `erp.company_writable(company_id)` (kernel migration `CompanyWritePermission`). It is true
     unless the transaction carries a write limit (`app.write_scope = 'list'`). Under a limit it is
     true only for the companies in `app.write_ids`, and only while `app.write_tx` is this
     transaction's start time. A limit carried over from an earlier transaction permits nothing
     (fail closed).
   - `ErpDbSession.BeginAsync` empties `app.write_scope` in its binding statement (no limit for
     system work, and none left over from a pooled connection). `LimitToPermissionAsync` sets the
     limit when the endpoint's permission is held only in some companies. It sets
     `app.write_ids` to those companies within the bound scope, once per request, and it can
     only narrow.
   - Every company table carries three RESTRICTIVE policies: `company_write_insert` (WITH CHECK),
     `company_write_update` (USING and WITH CHECK) and `company_write_delete` (USING), all with
     `erp.company_writable(company_id)`. `TenantSql.ProtectCompanyTable` now creates them, so a
     future company table gets them automatically. `TenantSql.ProtectCompanyWrites` adds them to
     the six existing tables (tenancy migration `CompanyWritePermission` for companies, branches,
     user_company_access, user_branch_access and user_workplaces; identity migration
     `CompanyWritePermission` for user_company_roles). The migrator's `SecurityInvariants` refuse
     to finish if a table with a `company_scope` policy lacks any of the three.
   - Reads do not change. Under a limit to X, Y's rows stay readable in a write request, so
     endpoints still answer 403 `permissionNotHeldInCompany` (with its English and Arabic text)
     rather than 404. An update or delete of a Y row affects no row, and an insert into Y, or
     moving a row into Y, fails with insufficient privilege.
   - The rule is the same as the C# guard's (`ErpDbSession.PermitsCompany`), so tracked writes
     behave exactly as before. The database adds the refusal for every other statement.
2. **Source gate.** Every `ExecuteUpdate` and `ExecuteDelete` in `src/` must sit in a file listed
   in `tests/Gates/set-based-writes.txt`, with the exact count and what it writes. Today there are
   ten uses in six files, none on a company table (`G2SetBasedWriteSourceTests`, with a self-test
   that plants the critic's P1 line). This also covers the rules that live only in the kernel's
   SaveChanges checks (branch limits on `ICompanyWide`, `IWorkspaceWide` records).
3. **Gates, one field at a time.** `CompanyRoleVariants` (called by `CompanyRoleRecords`) covers
   every endpoint that edits a record of company Y (its own GET answers the record as JSON) and
   every create whose body names Y. For each writable field, the manager of X sends one request
   with the record as read and that one field changed to another valid value (booleans flipped,
   an enum's other value, a fresh code, another country, phone, PO box, TRN, currency, month or
   day), and one request with the field left out. Y's record must not change, and Y must answer
   403 wherever the same variant is accepted in X. Every accepted change in X is put back by the
   administrator. If the manager of X cannot make a variant in X either, the administrator tries
   it. If the administrator can make it, the gate reports that it cannot tell a per-company check
   from a malformed request. A field without another valid value is reported as a blind spot,
   never skipped. Named checks were added for closing a branch of Y, closing company Y and
   switching Y's Arabic-legal-name setting (403 each), with closing and reopening a branch of X
   as controls.
   - `G2CompanyWritePolicyTests` checks the policies table by table: exact commands and
     expressions; under a limit to X, Y's rows are readable but not updatable, deletable,
     insertable or movable; X's rows stay writable; with no limit Y's rows are writable (the
     control); an unmarked limit permits nothing. A gate self-test plants a company table
     without the policies, and one with a permissive or wrong policy.
   - Self-test plants (`CompanyRolePlantModule`): `/branch-edits` is the critic's P1 with the
     database limit lifted (a product without this round's layer). The one-field variant
     `[isActive changed]` catches it, and no other variant does. `/branch-closes` is the same
     plant with the limit in place: the database refuses the closing statement, nothing in Y
     changes, and the X control still closes X's branch. The old city plant now lifts the limit
     too, since it stands for a product whose only checks are the endpoint's.
   - Ratchet: `g2.companyRoleVariants` 120 (124 measured) and `g2.companyRoleRecordRequests`
     850 → 1,500 (1,592 measured).

Alternatives considered: a ban on ExecuteUpdate/ExecuteDelete alone, without the database layer,
was rejected. Raw SQL and a set-based statement in a reviewed file would still bypass it. An EF
Core query interceptor that rewrites ExecuteUpdate filters was rejected for the same reason: it
does not cover raw SQL, and it would make the database's answer depend on the C# pipeline.

## Other findings of round 13

- **The companies list cut legal names short at 1,400 px.** A list column may now ask for a
  `Narrow` or `Wide` share (`ListColumnWidth`, additive: kernel `ListColumn.Width`, lists
  `ListColumnDto.Width`, web `ListColumn.width`). A wide column gets at least 14rem and 2.2 shares;
  a narrow one gets 6rem (or its header's room) and 0.6 shares. The companies list marks both
  legal names wide and the code and currency narrow. The branches list marks both names wide and
  the code narrow. Tested in `ListView.test.tsx` and `CompanyTests`.
- **Go-to chords cannot be chained from a list screen** (G B, then G W, types "gw" into the
  search box). The chords belong to the shell (p04), and the search box focus on open belongs
  to the list framework (p05). This is left to those pieces; Esc leaves the search box first.
- **Switch-company with seven or more active companies** (routed from p01 round 14). This was
  handled in round 13 (commit cb13e133): the driver's set-up switches to the target and back. The
  round-13 critic ran it with seven or more active companies and recorded a WIN.

## Cost

The new gate work is about 12 s of test time inside the gate project: the variants add about
10 s to `A_role_held_in_company_X_acts_on_nothing_of_company_Y`, the database checks take
about 1 s, and the source scan takes under 1 s. One extra round trip runs per request whose
permission is company-limited (the write limit statement). Requests with workspace-wide
permissions do not change.
