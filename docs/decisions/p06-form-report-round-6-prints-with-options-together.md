# p06 — Round 6: the print gates send options together; place names in Arabic documents

Date: 2026-10-17. Piece: p06-form-report. Status: accepted.

## The gap (critic p06 round 5)

Two faults planted in this piece's own print code passed every gate, because every gate printed
with one option at a time:

- **L7b** (tenant isolation): a record PDF kept for the browser's inline viewer, keyed on the
  report, its parameters, the language and the digits, with no tenant. Only a request that carries
  tenant B's record id *and* `format=pdf` *and* `disposition=inline` reaches it. The reports probe
  sent B's ids with a format and a language only.
- **P10** (permissions): the users-list print named role records to a caller who may not read roles
  when the print carried both `filter` and `sort` (what a saved view's print sends). The G2 report
  data check printed each documented value on its own.

## Decision

### G1: the reports isolation probe sends tenant B's records with every print option together

`ReportsIsolationProbe` (product code, `src/Modules/Reports/Erp.Modules.Reports/ReportsModule.cs`)
gains a combined phase, run whenever the gate hands it tenant B's administrator:

1. Tenant B finds **its own** records for every reference parameter of every report and every
   filterable reference column of every printable list, through the parameter's lookup list (at
   most three each). These are records B can print, so B's prints really hold B's data.
2. For each report and list it builds a **covering set of strength three** over the print options:
   format, language, digits, disposition, time zone, grouping, the report's own choices, flags and
   dates, B's record ids (a required reference has no "absent"), and for a list its columns, sort
   (both directions), search (B's own text) and filter (the documented example and B's ids). Every
   combination of the values of any three options occurs at least once, so the record, the PDF and
   the inline disposition meet, whatever else varies. Strength two was tried first: the probe's own
   planted-store test showed L7b's three-way key was then met only by chance.
3. Tenant B sends each request first; tenant A then sends exactly the same request, B's ids
   included, with and without tenant switch headers. Every answer goes to the gate whole, as before.

The covering set is built greedily and deterministically (`ReportsIsolationProbe.Covering`), so a
failing run can be repeated. This sits beside p00's generic replay in the answer-shapes phase
(p00 round 10, `docs/decisions/p00-foundation-g1-replay-and-statistics.md`), which also sends B's
own shape requests as A. The probe is this piece's own coverage of its own surface and does not
depend on what the generic phase enumerates.

Tests (`tests/Erp.Modules.Reports.Tests/ReportApiTests.cs`):

- `The_reports_isolation_probe_sends_tenant_Bs_own_records_with_every_print_option_together`: a
  planted print surface keeps L7b's store (inline record PDFs keyed on report, record, language and
  digits) and L7c's (list prints keyed on columns and sort together). The probe must fill both from
  tenant B, send an inline PDF of B's record for every report that names a record, and hand tenant
  A's answers holding B's markers to the gate. Without the combined phase neither store is ever
  asked for.
- `Covering_rows_hold_every_combination_of_the_values_of_any_options_of_that_many` (strength 2 and
  3): every combination is present, and the number of rows stays near the product of the largest
  sizes, far below every combination.

The critic's real L7b patch (`plant-L7b-inline-viewer-copy-by-record.patch`) applied to a copy of
this branch fails `G1HttpIsolationTests.Tenant_A_cannot_reach_tenant_B_through_any_endpoint` with
20 leaks and nothing else: the reports probe's combined phase ("probe reports and list printing:
observed tenant B marker") and p00's generic replay ("tenant B's own request for an answer shape,
sent by tenant A … response contains tenant B marker") each find it on their own. That run took
38 min of wall time under a load average of about 80 (the machine ran another workflow's verify and
this round's verify alongside); round 5's critic measured 22 min on a quieter machine.

### G2: report data prints documented values together

`ReportDataCheck` (`tests/Erp.Gates.Tests/G2/G2ReportDataTests.cs`) keeps every print it made (the
default and all-columns queries in both languages, each documented value on its own) and adds,
for every report and list and **every permission set**, the rows of a covering set of strength two
over the documented values (`ReportDataCheck.Combinations`): every value of every parameter meets
every value of every other parameter at least once, and every row gives every parameter a value
other than its default, so each row is also the saved-view shape (search, filter, sort, columns,
grouping, digits, time zone and disposition at once). Each runs in all four formats, in one
language, alternating with the single-value prints so both languages are covered across sets.

A row that groups by a column the caller's permissions withhold must be refused (400) in every
format, as before; the rest of that row is also printed without the grouping and judged, so a
caller without the column's permission is not left with nothing judged.

Self-test plants in `PrintPlantModule` (their own environment, as before):

- Bug 66, P10's shape: a list whose printout names the roles only when it carries a filter and a
  sort at once.
- Bug 67: a report that prints the companies' tax registration numbers only when two of its own
  flags are both true.

`ReportPrintSelfTests.The_report_data_check_prints_documented_values_together_and_catches_what_only_a_saved_view_or_a_pair_names`
requires the check to report each, and only on prints that carry the pair. The critic's real P10
patch applied to a copy of this branch fails `G2ReportDataTests.Every_report_prints_only_data_its_permissions_show_elsewhere`
(the users list printed by a holder of exactly `identity.users.read` with
`filter=isActive eq true&sort=-createdAt&…` names "Administrator" in the roles column, in JSON, CSV,
XLSX and PDF).

Ratchet: new `g2.reportCombinations` 110 (117 measured) and `g2.reportCombinationRuns` 1,000
(1,041); `g2.reportValuesJudged` 30,000 → 40,000 (46,400), `g2.reportFilesJudged` 4,000 → 6,500
(7,242), `g2.reportFileValuesJudged` 90,000 → 110,000 (126,638).

## Place names in Arabic documents (critic p06 rounds 4 and 5)

An Arabic company profile printed the country as "AE" and branch cities (Al Ain, Fujairah) in
Latin letters. `Erp.Kernel.Reports.PlaceNames` (kernel, additive):

- `Country(code)`: the country's English and Arabic names from the CLDR data in ICU, the
  globalization library the product already runs on (read as the region part of a culture's
  display name in the document's language; a code ICU does not know prints as stored). No list of
  country names is kept in the product.
- `City(name)`: the Arabic name of the UAE's cities and towns (the seven emirates' cities and the
  common towns: Al Ain, Khor Fakkan, Kalba, Dibba, Jebel Ali, Hatta, Ruwais, Madinat Zayed, Liwa,
  and others) by their usual English spellings; any other city prints as typed in both languages.
  City is free text on the company and branch records (p02); a separate Arabic city field would be
  p02's data model and is not added here. The record's Arabic address still prints in full when it
  has one.

The company profile prints its country fact and its branches' cities with them; the branch
directory prints its cities with them.

## Record print menu in the screen's language

The record form's print menu (Alt+R) now lists the PDF in the screen's language first, and focus
lands on it, as the list print menu already did. An Arabic user pressing Alt+R then Enter gets the
Arabic PDF.

## Not changed this round

- Money and decimal form fields are still used by no screen: none of the platform-core records
  (companies, branches, users, roles) holds an amount or a decimal quantity. They are exercised by
  the form kernel's own tests and come into use with the first business module.
- The TRN rule (exactly 15 digits, `docs/compliance/vat-trn-format.md`) and the per-company
  "Arabic legal name required" setting (`docs/compliance/arabic-legal-name-on-documents.md`) are
  p02's, already on the integration branch and shown by the company form. The company profile
  prints the stored, valid TRN only, and the Arabic legal name with the English one as the fallback
  the rule allows; with the setting on, the Arabic name always exists.

## Processor time

Measured on this machine:

- `G2ReportDataTests.Every_report_prints_only_data_its_permissions_show_elsewhere`: 51 s wall
  with the combinations (round 5's record measured about 78 s under load before them; the run
  without them was not repeated on a quiet machine). The added 1,041 runs × 4 formats are of the
  same size as round 5's 1,197 single-value runs, so roughly 100 to 150 processor-seconds.
- The new print self-test: about 8 s in the existing print plant environment.
- The probe's combined phase adds tenant B's and tenant A's combined requests to the G1 HTTP
  isolation test and to its leaky-module self-test (both load the reports module).
- Whole verify on this branch (`./erp verify` through the verify slot, slot taken 03:14:30Z): passed
  in 3,987 s of wall time on a busy machine (load up to 82.8 on 16 CPUs), processor time 9,725 s
  (dotnet 7,722, web 1,696, e2e 252, timing 55) against the 10,500 s maximum; the last integration
  and critic verifies before this round used 9,512 to 9,809 s, so this round adds at most about
  100 to 200 processor-seconds. Counts: 628 .NET, 478 web unit, 102 end-to-end, 373 comparison
  and 6 build tests; `suite.dotnetTests` and `suite.webUnitTests` minimums raised to 628 and 478.
