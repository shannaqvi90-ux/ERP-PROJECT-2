# p02 — TRN of 15 digits, and each company's setting that requires its Arabic legal name

Date: 2026-10-11. Piece: p02-tenancy. Status: accepted (round 10).

## Context

On 2026-10-10 the owner signed off two statutory rules, each recorded with its source in
`docs/compliance/`:

- `vat-trn-format.md`: a Tax Registration Number is exactly 15 digits 0 to 9. Spaces and dashes a
  user types are removed before the check, and anything else is refused. There is no prefix rule
  and no check-digit rule. Source: UAE Ministry of Finance, *Electronic Invoicing Guidelines* v1.1.
- `arabic-legal-name-on-documents.md`: the Arabic legal name stays optional (Arabic documents
  print the English one when it is missing). Each company has a setting, off by default, that
  makes it required. The English legal name is never saved empty.

The product until round 9 accepted any 1 to 20 digits (`^[0-9]{1,20}$`, column `varchar(20)`), so
"12345" was saved (p06 round 3). The lead asked that stored values which do not match are handled
without losing them, and that the column and the API stay in step.

## Decision

**TRN.**
- The API takes the number as typed. It removes every space and dash, then requires exactly 15
  digits (`tenancyTaxNumber`, in English and Arabic). Letters, dots, slashes, a plus sign,
  Arabic-Indic digits (٠ to ٩), 14 or 16 digits are all refused. The request is bounded at 100
  characters, so that an endless run of spaces is not taken in. That bound is not part of the rule.
- What is stored and answered is the 15 digits alone. The column is `varchar(15)` with check
  constraint `ck_companies_tax_registration_number` (`^[0-9]{15}$`). The OpenAPI document gives the
  request pattern `^[ -]*(?:[0-9][ -]*){15}$` and the answer pattern `^[0-9]{15}$`. The database,
  the validator and the documented contract all hold the same rule.
- Stored numbers that do not meet the rule are not lost. Migration
  `CompanyTaxNumberFifteenDigitsAndArabicNameSetting` keeps a number that is 15 digits once its
  spaces and dashes are removed, in that form. It moves any other number, exactly as it was, to a
  new column `legacy_tax_registration_number` and clears the TRN. Each change is in the audit
  trail as the migration's (`actor_kind = migration`). The owner reads past row-level security
  only inside the migration's transaction, as `AccessCounts` did, and FORCE is restored before it
  commits.
- The company record answers `legacyTaxRegistrationNumber`, which is read-only. The company form
  shows it under the empty TRN field ("The number on file, 12345, is not 15 digits and is not
  printed..."). The company stays editable: saving without a TRN keeps the old number, and saving
  a valid one clears it. A legacy number is never printed, because an invalid TRN on a document is
  worse than none.
- The Tax Identification Number (the first 10 digits) is derived from the TRN and never entered,
  so the product has no field for it. No module uses it yet.
- Arabic-Indic digits are refused, because the rule says "0 to 9, nothing else". Whether they
  should be turned into 0 to 9 when typed on an Arabic keyboard is a question for the owner. It is
  listed as a human gate and not decided here.

**Arabic legal name setting.**
- Each company has `arabic_legal_name_required` (boolean, default false). It is part of the company
  record, so it needs the same permission as the rest of the record (`tenancy.companies.update`,
  and every branch of the company). It is not a new permissioned action. The G2 gates, the screen
  gate (`screens.test.tsx`: the setting is a field that disappears without the update permission)
  and the G1 write gates (the field is in the request body they vary) cover it with no new rule.
- The request field `arabicLegalNameRequired` is optional. When it is left out, a new company has
  the setting off and a changed company keeps its current value, so older clients (the comparison
  drivers, scripts) keep working.
- While the setting is on, a company with a blank Arabic legal name is refused
  (`tenancyLegalNameArRequired` on `legalNameAr`, in English and Arabic). This covers creating it
  so, emptying the Arabic name, and turning the setting on for a company with no Arabic name. The
  database holds the same line: `ck_companies_legal_name_ar_required` is
  `NOT arabic_legal_name_required OR btrim(legal_name_ar) <> ''`.
- The form shows the setting under the legal names. While it is on, the Arabic name is marked
  required and its hint says why.
- The English legal name rule (`p02-tenancy-english-legal-name.md`) is unchanged: it is required,
  and the API and `ck_companies_legal_name` refuse a blank one.

**From the round 9 verdict in the same change.**
- *Companies screen, user who works in no company (the round's biggest gap).*
  `worksInEveryCompany` (records.ts) is the one place the screens decide this. It fails closed: no
  company row means no, and a row that does not say `everyCompany: true` is no too. The Companies
  screen and the palette's "New company" (now with `available`) both use it. `screens.test.tsx`
  models a user with no company rows and one whose rows say nothing. With every permission, New
  is absent and Alt+N and `?open=new` open nothing; a control shows New for a user who works in
  every company. `scripts/tenancy-plant-self-test.mjs` has 4 more plants (U5, the critic's U5b,
  U5c, U5-palette), 28 in all, and the gate catches every one. `GrantRulesTests` checks the server
  side of the same state: an Administrator with no company reads no row, and a create is refused.
  We did not add `everyCompany` to a response outside the rows. That would add a field (or an
  endpoint) every G1/G2 run attacks, and the processor-time budget is spent. The fail-closed read
  and the gate cases cover the fault class.
- *Order of refusals on create.* `POST /companies` now answers 403
  `tenancy.companyNeedsEveryCompany` before it judges the body, so a user who may not create a
  company learns that at once.
- *Arabic keyboard path.* On an Arabic screen the company form puts the Arabic legal name first,
  and a new company starts there, as the branch line already did. A branch whose Arabic name was
  typed but whose English name was left at the company's name is saved with no English name. It
  is not the head office, and its English name is unknown, so it gets a code made from the
  fallback (BR-1, ...) and not HQ. A line left at the company's names in both languages is still
  the head office.

## Not done this round

- Switching company with more than six active companies on a fresh device still shows no
  one-click buttons until the first switch on that device. Filling the buttons with arbitrary
  companies would go against the round 4 decision ("no arbitrary chips"). Keeping recent companies
  on the server needs a new column that every gate run walks. For now, Alt+C, type, Enter is the
  path with many companies.

## Processor time

This round adds about 3 s to `./erp verify`. That is 3 .NET tests (one rolls back two ALTER TABLE
statements on its own test database), a few lines in two existing ones, 5 web unit tests (about
1.5 s), one migration, and two body fields the G1/G2 gates vary. The gates enumerate the fields
from the OpenAPI document, so they need no new run. The plant self-test (`npm run
test:tenancy-plants`) is not part of verify.
