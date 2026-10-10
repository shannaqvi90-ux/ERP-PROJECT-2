# p02 tenancy: users that companies share, and company-level actions on screen

Round 9. It closes critic p02 round 8's biggest gap and round 7's biggest gap. Both rounds were
judged after the fact, so the two verdicts arrived together.

## Round 8: a user who works in X and Y is a record both companies share

### The fault

`IUserWorkplaces.SetAsync` (tenancy's public contract for another user's default company) refuses
a user who also works in companies outside the caller's scope (`UserBeyondScope`). The critic
removed that check (plant PU), and every gate stayed green. The company gates' callers always
worked in X and Y, so no attack aimed an administrator of X alone at a user who also works in Y.
Since then, p03 has added the same count to identity's target check
(`identity.userWorksBeyondOwnCompanies`, read from `IUserWorkplaces`). That is one mechanism, not
two: tenancy keeps the count, and identity asks for it.

### Decision

1. The company layer of G1 gets user targets (`SharedUserRecords`, run by
   `G1CompanyIsolationTests.An_administrator_of_company_X_leaves_alone_the_users_who_also_work_in_company_Y`).
   It uses the same environment as the company attack, so no new database or host starts.
   - The targets are created fresh. One works in X and Y and holds a workspace-wide role. One works
     in X and Y and holds its roles in X and in Y (company roles). The control works in X alone, in
     two of its branches, and holds the same workspace-wide role. None of them has a default company.
   - The writes come from every module: each POST, PUT, PATCH or DELETE whose route parameters are
     all ids is sent with the target's id in each one. A later module's user-addressed write is
     therefore attacked without anyone listing it.
   - Each body is the endpoint's own read of the target with one field changed:
     - a text, switch or choice changes as it does for the company's shared records;
     - an id becomes company X (or Y, where it is X already);
     - a list loses its last item, the first list of two or more, searched depth first: a user's
       companies, a company's branches, company roles.
     Where there is no read, a valid body is used.
   - The tenant's administrator proves each write. It must succeed and change the target's rows (in
     any tenant table outside the audit trail, a row whose `id` or `user_id` is the target's), and
     it is then undone. DELETEs are not proven. They are judged by the target's rows.
   - Then the administrator of X alone sends every proven write. Aimed at either X-and-Y target,
     each must be refused (no 2xx, no 5xx), and those targets' rows must be exactly as they were.
     Aimed at the control, the named writes must succeed, so a refusal is about where the target
     works and not about a malformed request.
   - The named writes must be proven on every target: the default company, company access, and the
     profile (`displayName`). The company-roles target must also have `companyRoles` proven.
     Ratchet `g1.companyUserTargetWrites`, which counts writes proven on the two X-and-Y targets,
     starts at 19 (9 + 10 on this commit).
2. A contract check on the tenancy service:
   `The_default_company_contract_refuses_a_user_who_works_beyond_the_callers_companies`. In a unit
   of work bound to X alone, it resolves `IUserWorkplaces` and calls it directly. Other modules
   reach the service without identity's endpoint in front of it, so the service must refuse on its
   own. For a user of X and Y, the check expects:
   - `GetAsync` reports `CompaniesElsewhere`, and the companies it lists are X alone;
   - `WorkingElsewhereAsync` contains the user;
   - `SetAsync` answers `UserBeyondScope`, both to X and to none.
   A user of X alone is changed. After commit, the database shows no workplace row for the first
   user and X for the second.

### Plants (run by hand on this commit, then reverted)

| Plant | Gate run | Result |
|---|---|---|
| PU: the 4-line `if (elsewhere) return UserBeyondScope` removed from `UserWorkplaces.SetAsync` | the two new tests | contract test fails (`Assert.Equal` failure: Done, expected UserBeyondScope); the HTTP test passes, because identity's check now refuses first |
| PU together with identity's `userWorksBeyondOwnCompanies` check removed | user-target attack | fails with 8 writes answered 2xx, `PUT .../default-company [companyId]` among them (password, sessions/revoke, unblock, displayName, language, isActive, email) |
| `tenancy.userBeyondOwnCompanies` removed from `CompanyAccessRules.RefuseUserAsync` | user-target attack | fails: `PUT /api/tenancy/access/{userId} [companies]` answered 200 for both X-and-Y targets |

## Round 7: New and the company code offered to administrators of some companies

### The fault

Company codes are unique across the workspace, so the server lets only someone who works in every
company create a company or change a code (`tenancy.companyNeedsEveryCompany`). The Companies
screen judged New and the code field on the permission alone. `screens.test.tsx` modelled
`everyCompany` only for the Workspace screen.

### Decision

- The API says whether the user works in every company. `everyCompany` is on the company record
  and on every row of the companies list (the same value on each row), computed with the
  server's own rule (`CompanyAccessRules.ScopeHoldsEveryCompanyAsync`, one primary-key read).
- The Companies screen reads one row (`?take=1`) before it draws. So New, Alt+N and the record
  panel's "new" are offered only to someone who works in every company, from the first frame. A
  keyboard user, or the comparison harness, never meets a New key that appears late. No row
  means the user works in no company (every workspace has one), so New is not offered.
- On the company form, the code is read-only for anyone else, with the reason in English and
  Arabic: "Only someone who works in every company of the workspace may change a company code."
- The list opens the record its address names (`<screen>/new`, typed or bookmarked) once its
  definition arrives, even where the screen offers no New. The Companies screen ignores a new
  record from the address unless it offers New. The guard sits in the screen, not in the shared
  record panel: a guard in the panel would also neutralise identity's planted fault U1-shortcut
  (`panel.onOpenIdChange(newRecord)`), which verify's identity plant self-test then reported as
  passing.
- Gates:
  - `screens.test.tsx` checks a user of some companies: exactly New and `field:code` disappear;
    Alt+N and `?open=new` open nothing; the reason is shown.
  - `tenancy-plant-self-test.mjs` gains three plants: `U-company-new-some-companies`,
    `U-company-code-some-companies` and `U-company-address-new`. `U-company-new` was updated to
    fit the new line. All 24 plants are caught.
  - A new end-to-end test signs in an administrator of ALN-SHJ alone on the real stack.

## Also in this round

- On the company form, Ctrl+S or Ctrl+Enter on a branch line that holds a typed branch adds the
  branch, as Enter does (critic round 8: Ctrl+S left the line pending). An untouched line, holding
  only the company's name, leaves the key to the company form.
- The switcher still shows one-click chips only for recently used companies when there are more
  than six. On a fresh device with seven or more active companies it offers none, which is the
  deliberate round 4 design ("no arbitrary chips", WorkplaceSwitcher.test.tsx). The critic's
  round 8 run reached that state only after its own walk left seven active companies. Changing
  it needs a recent-companies record kept on the server; that is not done this round.

## Processor time this round adds to `./erp verify`

- The two new gate tests run in the existing `G1CompanyFixture` environment: about 8 s of wall
  time and roughly 10 to 15 s of processor time (host plus database), measured locally.
- The Companies screen sends one extra small list read when it opens.
- Two web unit tests, one more companies test and one end-to-end test add about 10 to 20 s of
  processor time.
- In total, under about 40 s of the 10,500 s maximum.
