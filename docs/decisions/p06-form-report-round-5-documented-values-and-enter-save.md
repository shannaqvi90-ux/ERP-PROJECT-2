# p06 — Round 5: prints judged with every documented value, company roles in print, Enter saves

Date: 2026-10-09. Piece: p06-form-report. Status: accepted.

## 1. G2 prints every report and list with every documented query value

Critic p06 round 4 planted P9: a users list print that names role records to a caller who may
not read roles, but only when the print carries `search`. G2's report data check printed each
list with only two queries (default columns, all columns) and each report only without
parameters or with a record, so P9 passed.

Decision: `ReportDataCheck` now also prints every report and every printable list, for every
permission set it already judges, with a value other than the default of each documented query
parameter, one parameter per print, in every format (JSON, CSV, XLSX, PDF):

- every value of every enumerated parameter except format and language (digits, disposition,
  grouping, a report's choices);
- every documented value of the others, from the same function G1's answer-shapes phase uses
  (p00 round 8, `IsolationAttack.DocumentedValues`): each example the API document gives and
  its parts (one column, the first half of the columns), the other direction of a sort, both
  answers of a flag, two dates, another time zone;
- a search for the word of the list's rows that finds the most rows, and the first row's word.

A free-text query parameter with no example and no such word is a problem in itself (the check
would be blind to it), so every list print route now documents a filter example (the first
built-in view's filter, else a condition on the first filterable flag or text column), and a
report without groupable columns documents the empty `groupBy`. Grouping by a column the
caller's roles withhold must be refused in every format (it was, with 400; the check now expects
it explicitly).

The default and all-columns prints still run in both languages. Each documented-value print runs
in one language, alternating from one permission set to the next, so every value runs in both
languages across the sets. This halves the added cost; a fault that needs one particular
permission set, one particular value and one particular language together could be missed, and
that is the one thing this trade gives up.

Plants: self-test bug 64 (a list whose printout names roles only when searched, P9's shape) and
bug 65 (a report that prints tax numbers only with `detailed=true`) are caught by
`ReportPrintSelfTests`. Critic r4's real P9 patch applied to the product was caught by
`G2ReportDataTests` (users list, `search=a`, a caller with only `identity.users.read`).

Ratchet: `g2.reportQueryVariants` 130 (133 measured) and `g2.reportVariantRuns` 1,150 (1,197);
`g2.reportValuesJudged`, `g2.reportFilesJudged` and `g2.reportFileValuesJudged` raised to 30,000,
4,000 and 90,000 (measured 37,682, 4,407 and 104,947).

Processor time: `G2ReportDataTests` went from about 28 s to about 78 s of wall time on one
environment it already had (no new environment, no new stack); the new self-test adds about 14 s
in the existing print plant environment. Roughly 100 to 150 processor-seconds added to a verify.

The print-store tenant leak of round 4 (L6) is G1's: p00 round 8 made the answer-shapes victim
ask with documented values (self-test bug 60). The report routes give it what it needs: columns,
filter and sort examples, a time zone example, and now a filter example for every list.

## 2. Company-scoped values print as the screen shows them

Critic p04 round 7: the users list on screen reads "Staff, Company manager (ALN-DXB), Read only
(ALN-FZE)", but its PDF printed only "Staff". The print used `roleIds` alone.

Decision: `ListColumn.InCompany` (kernel, additive) names a row field holding values assigned in
one company each (`companyRoles`: `roleId`, `companyId`) and the flag saying more are held in
companies the reader does not work in (`rolesElsewhere`, printed as the screen's "roles in other
companies"). The reports module names them "name (company code)" with the codes of the
companies in the caller's company scope (`ICompanyDirectory.ListAsync`, the same companies
`/api/identity/companies` gives the screen). A caller who may not read roles still gets a count,
which now includes the roles held in one company. G2's list corpus counts those company codes
as shown, since they are the caller's own companies.

The rule is declarative in the list definition rather than code in the identity module, so the
permission rule for naming other records (`ValuesFrom`) stays in one place, in the reports module.

## 3. Arabic documents name the printer in Arabic

`UserSummary` carries `DisplayNameAr` (additive), and documents print `NameFor(language)`: on an
Arabic document the Arabic name when the user has one. Before this, an Arabic document said
"printed by Mariam Al Mansoori" while the shell's browser print said "مريم المنصوري".

## 4. Enter in a one-line field saves the record

Critic p01 round 8 and p06 round 4: edit-and-save tied Odoo on keystrokes (19 against 19: our
Ctrl+S counts two keys), and a tie is a loss.

Decision: in a record form, Enter in a one-line input (text, e-mail, phone, number, date and
the like) saves, as Ctrl+S does. Excluded: a lookup (role combobox: Enter picks from its list),
a multi-line text (Enter is a new line), checkboxes, buttons, selects, read-only or disabled
inputs, Enter with any modifier, and Enter while an input method is composing. A read-only form
never saves. Browsers already submit a form on Enter in a text field; making it explicit means
it works the same in every browser, can be tested, and cannot slip past the permission checks
(plant W2-enter-field in `forms-plant-self-test.mjs` is caught). The save button's tooltip and
`aria-keyshortcuts` list Enter.

This is also what people expect in a dense data-entry screen: finish typing, press Enter. The
ours driver's default path for edit-and-save is now click, type, Enter. In this round's verify
health check (`node run.mjs --task built --product ours --health` against the verify stack) it
verified at 3 steps, 18 keys, 0.17 s machine and 8.09 s modelled human time; Odoo's last
recorded best (p01 round 8) is 4 steps, 19 keys, 0.343 s and 10.28 s. The Ctrl+S and pointer
paths stay as variants. A side-by-side run against the Odoo rig is the critic's to make.
