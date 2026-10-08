# p06 — The permission gate reads every file and every printed list (round 4)

Date: 2026-10-08. Piece: p06-form-report. Status: accepted.

## Why

Critic p06 round 3 planted two permission faults in this piece's own export and print code and
every G2 test passed:

- P6: CSV and XLSX exports ignored the columns the caller's roles withhold (the users-by-role CSV
  carried Role and Only in company for a user without `identity.roles.read`).
- P8: a list printout named another list's records without checking that the caller may read that
  list (the printed users list named each user's roles instead of counting them).

`G2ReportDataTests` judged reports only as `format=json`, and nothing judged
`/api/reports/lists/{key}`.

## What the gate does now (`ReportDataCheck.RunAsync`)

Every report and every printable list runs in all four formats (JSON, CSV, XLSX, and PDF read for
its text with PdfPig) and in English and Arabic. Each runs as a user holding exactly its permission,
then again with each other read permission added (36 report sets and 45 list sets today). On top of
the existing rule (every printed value of the workspace's data must also be shown by an endpoint
those permissions open), these rules now apply:

1. **A file is its document.** A CSV or XLSX carries exactly the JSON document's column titles (the
   group's first, only when the grouping column is not printed already). Every workspace value a
   file prints must be in the JSON document of the same request, unless the document stopped at its
   row limit and the export did not. A file answers with the JSON's status, so a parameter refused
   to the caller is refused in every format.
2. **A report document leaves out every column whose permission the caller lacks.** The catalogue
   test already checked this for the catalogue; the document itself is now checked too.
3. **A printed list prints the list.** Every workspace value it prints (apart from the letterhead,
   which is judged like a report's) must be in what the list's own endpoint answers that caller,
   plus the rows of any list named by a `ValuesFrom` column whose permission the caller holds. A
   `ValuesFrom` column the caller may not read prints a count, never names.

Why a list is judged by its own rows and not by every endpoint: `GET /api/identity/users/{id}/access`
(under `identity.users.read`) shows the names of a user's roles, so "shown by some endpoint" would
pass P8. The product's own rule is narrower: a printed list shows what the list shows, and names
from another list only to a reader of that list. The gate now checks that rule.

The product prints its own words, and a word can contain a value of the data ("Inactive" contains a
status value "active"). An occurrence of a value inside one of the product's resource strings at
that spot is that word, not data printed. Any other occurrence still counts, so the plants are still
caught.

## Proof

- On a local copy, the critic's patches `plant-P6-exports-ignore-withheld-columns.patch` and
  `plant-P8-list-print-names-without-permission.patch`, each applied alone, fail
  `Every_report_prints_only_data_its_permissions_show_elsewhere`. P6 fails on the column titles and
  on values not in the JSON document. P8 fails on the count rule and on values the list does not
  show.
- The self-test `ReportPrintSelfTests` plants both shapes in a module of its own
  (`PrintPlantModule`, in its own environment): `printplant.exportOnly`, whose exports add the
  companies' tax numbers, and `printplant.people`, whose printout names the roles that the list
  hides. The first try put them in the leaky module, but every endpoint there is attacked by the
  HTTP isolation self-test. That stretched it from 44 to 58 minutes, and it failed on the new routes,
  which sit outside `/api/leaky/`. The separate environment costs about 12 s.
- Ratchet: `g2.printedListPermissionSets` 45, `g2.reportFilesJudged` 936,
  `g2.reportFileValuesJudged` 45,000, and `g2.reportValuesJudged` raised from 2,500 to 15,000.

## Cost

On the WSL host, Debug build, the test took about 66 s and the whole run about 90 s. The 936 extra
answers account for about 27 s of server time. Judging the values takes almost nothing. To stay
under the 9,000 processor-second maximum, it shares the G2 fixture and the users of the existing
check: one user per permission set, and one corpus per permission.

## Other round 4 changes in this piece

- Roles and access (`identity.roleSummary`) counts users the way the roles list does: holders in
  every company plus holders in one of the caller's companies, each user once. Both use
  `RoleEndpoints.UserCountsAsync`, so the two cannot drift apart again.
- Exports write a grouping column once. The branch directory, grouped by company, exported
  "Company,Company".
- A user's, company's or branch's status prints as Active or Inactive (نشط / غير نشط), not Yes or
  No. This uses the same choices on a flag column that a role's type already uses.
- OpenAPI: report and list-print routes describe 200 as the `ReportDocument` JSON and as PDF, CSV
  and XLSX binaries. A second `Produces` for 200 had replaced the first and left no content.
- The list grid leaves Alt keys to the application's shortcuts, so Alt+PageDown and Alt+PageUp move
  the open record right after Enter opened it.
- The browser printout of an on-screen report hides the screen's own letterhead and footer, so the
  report's letterhead and "printed by" line show once. Cells made only of digits and their signs
  (phone numbers) do not wrap.
- Forms kernel: p01 r7 (79792c5) added a second way to keep a record the form has just created. The
  kernel already had one (`justCreated`), and with both, the kernel's test "another record or a
  reload is read" fails. The kernel keeps its own way. p01's test passes on it and stays.
