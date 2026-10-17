# p02-tenancy: each permission writes only its reviewed write set (G2)

Round 11. Critic p02 round 10 (verdict BLOCKED, biggest gap).

## Problem

G2 checked which permission each endpoint declares, and attacked that declaration from every
side, but no gate checked what a request authorised by a permission writes. The critic's plant
P1b added `arabicLegalNameRequired` to `PUT /api/tenancy/workplace` (declares only
`tenancy.workplace.switch`) and set the company's Arabic-legal-name requirement from it. All 142
product gates passed, and on the live product a user holding only the switch permission changed a
legal setting of the company that otherwise needs `tenancy.companies.update`. "Every action is
denied unless a role grants it" was broken inside an endpoint.

## Decision

A new G2 gate, `tests/Erp.Gates.Tests/G2/WriteSets.cs` (`G2WriteSetTests`), with a reviewed file
`tests/Gates/permission-write-sets.txt`:

- Every permission has a write set: lines `<permission> <schema.table> <actions> <columns>` with
  the reason. Columns are `*` or an explicit list where the permission covers only part of a row:
  `tenancy.tenant.update` may change the workspace's names and settings but never its code or
  status (the platform operator's); `identity.profile.update` may change the caller's language
  and digits but never their name, e-mail or active flag; `identity.users.update` never the
  password (another table). `tenancy.workplace.switch` covers `tenancy.user_workplaces` only.
- For every endpoint that changes data (from routing, not from source), the gate creates a user
  holding exactly the endpoint's permission, working in every company and every branch (so no
  company or branch condition hides a write: P1b wrote only for a caller holding every branch).
  It sends requests that pass validation and run to the end: a create; an edit of a fresh record
  the administrator made for it (fresh values, the record's own version and references, so the
  edit changes something); a delete; an action on "all that match" narrowed to one fresh record;
  then the same request once per true-or-false field turned the other way and once per other
  documented value of every choice field. Records a caller's own permission creates (saved views,
  passkeys) are made by the caller.
- What each request wrote is read from the audit trail: every row change after a mark taken
  right before the request, whoever wrote it, with its table, action and changed columns. The
  audit trigger runs on every audited table whatever the write path (EF, raw SQL, a security
  definer function), so a side door cannot write around it; the tables without it are reviewed in
  `tests/Gates/audit-exempt.txt` (the audit trail and the sign-in history, both append-only). The
  gate's environment is its own, so no other test writes during a request.
- Findings: a change outside the write set of the endpoint's permission (and the `*` lines, none
  today); an endpoint the run never gets an accepted answer from, unless reviewed in
  `tests/Gates/write-set-unexercised.txt` (empty today: all 57 endpoints that change data are
  reached); a write-set line no request used (a set lists only what its requests write); a line
  naming a permission the catalogue lacks.
- If an endpoint answers nothing with exactly its permission (a shared view also needs its list's
  own permission), the gate retries it with every read permission added; its writes are still
  judged against the endpoint's own permission alone.
- Self-tests: `The_write_set_check_catches_a_switch_that_changes_a_company_setting` hosts a planted
  module (`WriteSetPlantModule`, P1b's shape, writing through the request's own unit of work) over
  the gate's database and requires the finding. The critic's own planted file
  (`gauntlet/evidence/p02-tenancy/r10/plants/A2-WorkplaceEndpoints-planted.cs.txt`) was also put in
  place of `WorkplaceEndpoints.cs` once by hand: the gate reported
  `PUT /api/tenancy/workplace [valid] as a user holding exactly [tenancy.workplace.switch] wrote
  update tenancy.companies (arabic_legal_name_required), outside the write set of
  tenancy.workplace.switch`. A pure test checks the judgement (permission, table, action, columns)
  and a reviewed-file test keeps company tables to company permissions.
- Ratchet minimums: `g2.writeSetEndpointsExercised` 57, `g2.writeSetWritesJudged` 410 (417 on this
  branch), `g2.writeSetLines` 28.

## Why the audit trail and not the SQL statements

The audit trigger records the effect (the columns that really changed, old and new), not the
statement text; a statement that names a column but leaves it unchanged is not a write, and a
function or trigger that writes elsewhere is still seen. Parsing SQL text would miss writes made
inside database functions and would have to understand every statement shape EF sends.

## Limits, stated

- A write that happens only for an input the gate never sends (a string of a particular shape, a
  date in the past) is not reached. The variants cover every true-or-false field and every documented
  choice; other inputs take the valid values the other gates use.
- Lines are checked per permission and table; an action listed in a line that no request made
  (`update` on `tenancy.user_workplaces`, which happens on a second switch) is allowed by review.

## Processor time

The new gate class runs in about 25 s wall time on this machine (57 endpoints, 343 requests, two
gate environments copied from the template: the run's own and the planted host over the same
database). Its processor time is a small share of that (the requests are sequential); against the
verify's 10,500 s maximum it adds well under 1 %. The other round 11 changes add web unit tests of
a few seconds and one .NET test assertion block.

## Other round 10 findings handled in round 11

- TRN typed in Arabic-Indic digits: converted (owner decision 2026-10-17), see
  `p02-tenancy-trn-and-arabic-name-setting.md`. The TIN (the TRN's first 10 digits) is now shown,
  derived, under a 15-digit TRN on the company form.
- A branch typed in Arabic only: the branch line says it was added without an English name and
  where to add one; the company's branch table and the branches list mark it "No English name" on
  English screens; the branch form asks for it. The English name stays optional: the head-office
  rule of round 9 (no company name saved as a branch's English name) is kept.
- The Arabic legal-name refusal: the field no longer shows its hint and the refusal at once, and
  form fields align by their labels and inputs (`align-items: start`, checkboxes level with the
  inputs) instead of by their bottoms, which pushed the English name down beside a tall neighbour.
  This is the shared form grid (`web/src/kernel/forms/forms.css`, and the same rule kept in
  `tenancy.css`); every form gains the same alignment.
- Palette: on the companies screen, "print compan" found nothing, because the module's entry
  "Print or export the companies list" is listed once, as the screen's own "Print or export the
  list", whose words do not name companies. The screen's entry is now found by the module action's
  words too (`CommandPalette.tsx`).
- The companies list's Arabic legal-name column cut at the pane's edge while a company is open:
  unchanged, for the reason in `p02-tenancy-trn-and-arabic-name-setting.md` (the shared list
  framework keeps column minimums and scrolls inside its pane; the splitter, or Alt+W, changes
  how wide the form is).
- Country on company and branch addresses is a list of countries named in the screen's language,
  the UAE and the Gulf first (`CountryField`, on the integration branch from p06, commit
  5b41c3da); round 11 adds a company-screen test on an Arabic screen.
