# p06 — Round 8: edit and save in twelve keys, the saved notice under its field, and another area's report data per company

Date: 2026-10-18. Piece: p06-form-report. Status: accepted.

## The gap (critic p06 round 7)

Edit and save tied Odoo on keystrokes. Ours: click the phone field, type `+971505550199` (13
characters, 14 keys with Shift); the last digit saves the record. That is 2 steps and 14 keys.
Odoo: triple-click the phone field, type the same `+971505550199`, click Save. That is 3 steps
and 14 keys, and the instrument credits Odoo's stored `+971505550199` because it compares values
with separators ignored. Ours won steps, machine seconds and modelled seconds. Keystrokes tied,
and a tie is a loss. The same task tied in rounds 6 and 7.

## What changed in the product

### The country code typed again is the same number (`web/src/kernel/forms/phone.ts`)

The phone field keeps `+971 ` and a click selects the number after it. Someone who types the
whole international number without its `+` (`971505550199`) now gets `+971 50 555 0199`. Before,
it became `+971 971505550199`. This is twelve keys, none of them shifted: the `+` (Shift and `=`)
is gone.

- Only a complete number is cut. The repeated `971`, or `0971` / `00971`, is dropped once what
  follows it is a whole UAE mobile or fixed-line number. Until then the digits stay as typed, so a
  real Fujairah line that starts 971 after the area digit (`9 71x xxxx`) is never cut.
- Eight digits of such an entry (`9715 0555`) are also a whole Fujairah fixed line. If that
  completed the entry, the form would save the wrong number halfway through. So a number after
  +971 that starts `971` never completes the entry on its own (`completesOnEntry`). It is saved
  with Enter or Ctrl+S like any other value. The 12-digit entry completes at its last digit,
  because by then it is `+971 50 555 0199`.
- A completed entry is now detected by counting the digits in what the field showed against what
  it holds after one more key, Arabic-Indic digits included. Before, it counted the national
  digits, and those shrink when the repeated code is dropped. A paste or a replaced selection is
  still never a typed completion.
- An editable phone field with no number shows `+971` as its placeholder, so every record shows the
  prefix, an empty one included.

Measured on this branch with the harness (`node run.mjs --task edit-and-save --product ours
--repeat 3` against a private stack): verified 3 of 3, **2 steps, 12 keys**, 0.19–0.39 s machine
time, 6.41 s modelled. The variants `compact` (`+971505550199`, 14 keys) and `spaced` still run as
well.

### The saved notice and Undo sit under the field (critic p06 round 7, other findings)

At 1400×900 the "Saved: the entry was complete. Undo" notice was 398 px above the visible part of
the form. The notice now sits under the field whose completed entry the form saved
(`FieldBinding.entrySaved` and `undoEntry`, drawn by `PhoneField` through `Field`'s new `notice`
slot). It is `role=status`, its Undo shows Alt+Z, and the form's own notice at the top is kept for
saves the user makes. An e2e test checks at 1400×900 that the notice and its Undo button are fully
in the viewport. Unit tests cover the 12-key entry, the single save at its last digit, the Undo
button and the Fujairah case.

## Edit and save: the paths tried, and why ours cannot be shorter under the current instrument

Tie rule (owner, 2026-10-17, needs-human #21; `gauntlet/goal.md` bar item 2).

Under the instrument as merged (`containsValue`), the entered value `+971 50 555 0199` counts
only if its letters and digits were all typed in order: `971`, `50`, `555`, `0199`, which is 12
digit keys. Separators, including the `+`, are ignored. So 12 keys is the least any product can
type. Ours now types exactly those 12 keys, with no `+`, no select-all chord and no save key.

| Path tried (ours) | Steps | Keys | Credited? |
|---|---|---|---|
| click > `+971 50 555 0199` > Enter (round 6) | 3 | 18 | yes |
| click > `+971505550199`, last digit saves (round 7) | 2 | 14 | yes |
| **click > `971505550199`, last digit saves (this round)** | **2** | **12** | **yes (measured 3/3)** |
| click > `505550199`, the field's own +971 kept, last digit saves | 2 | 9 | **no**: "the end state gained "+971 50 555 0199", which the measured part never entered" (run on this branch) |
| Steps below 2: the record would have to open with the phone field focused and its number selected | 1 | 12 | not taken: no form should open on its phone number |

Odoo can also type the 12 digits without the `+`. It stores the text as typed (critic round 7:
`505550199` and `+971505550199` were kept raw), so `971505550199` would be stored and credited.
Its shortest path stays triple-click, type, click Save: 3 steps and 12 keys. If so, ours wins
steps (2 against 3), machine seconds and modelled seconds, ties keystrokes at the instrument's
floor, and is worse on no metric. Ours also stores the normalised international number, where Odoo
keeps what was typed.

To win on keys as well, the only shorter path is the national entry: 9 keys, with the `+971` the
field already shows kept. Ours already supports it, and ours stores the full `+971 50 555 0199`
for it. Odoo stores only `505550199` for that entry, so Odoo cannot be credited for it. The lead
decided (this workflow, sent to p01) that the instrument may credit a fixed prefix the field
shows as its own, under these conditions:

- it is the product's default for every record;
- it is shown before the clock;
- the whole stored number is checked;
- the same rule holds for Odoo.

When p01's change lands, the ours driver should take the national path: a `national` variant
typing `505550199`, as the driver's comment says. It is not in the driver yet. A variant the
instrument refuses makes the whole ours result unverified (`runner.mjs`: every variant must
verify), and p01's change was not on the integration branch or on p01's branch when this round
ended.

## Reports: another area's data only where its permission is held (p02 round 12)

p02 made a permission count only in the companies whose roles grant it. The kernel narrows a
report's reads to the companies where the report's own permission is held. A column, fact or
parameter of another area was still judged by `caller.Has`: the working company's roles plus the
workspace-wide ones. The new gate below confirmed three leaks on the previous code:

- A user who read companies in X and Y, but branches only in X, printed company Y's profile with
  Y's branches.
- A user who read branches in X and Y, but companies only in X, got Y's legal name in the branch
  directory (12 prints).
- A user who read users in X and Y, but roles only in X, got the name of a role held in Y in users
  by role. The catalogue also offered that role column, parameter and grouping.

Decision:

- `HeldWhereReadAsync` (reports module): another area's permission counts only when it is held in
  every company the report reads, that is the scope's companies where the report's own permission
  is held. It can be held workspace-wide or by a role in each of those companies. The session must
  hold it too. The catalogue and the run use the same rule, so the screen never offers a column,
  parameter or grouping the run would refuse.
- `ReportDefinition.CompanyParameter` (kernel, optional, additive): a report about one company
  names its company parameter, and another area's data is then judged in that company alone. The
  company profile uses it, so X's profile still prints X's branches. A definition check refuses a
  company parameter that is not a required reference parameter.
- The branch directory prints a company's legal name only for companies where the caller reads
  companies (`ICompanyPermissions`). Elsewhere it prints the company's code, as it already did for
  a caller who reads no companies.

Considered and not taken: judging each row by its own company. That works for the branch
directory, which does it, but a column must be present or absent for the whole document, and
"withheld" is already the document's own note.

### Gate

`G2CompanyRoleTests.A_report_prints_another_areas_data_only_in_the_companies_where_that_permission_is_held`
(`tests/Erp.Gates.Tests/G2/G2CompanyRoleReports.cs`). It is a partial of `G2CompanyRoleTests`, so
it shares that class's environment and starts no new database. It prints the three reports in
JSON, CSV, XLSX and PDF, in both languages, as users holding the report's permission in X and Y
and the other area's in X only. It also checks the catalogue and that the role grouping and the
role filter are refused (400). Controls show the same data printed where the permission is held.
That is 59 checks, ratchet `g2.companyRoleReportChecks` 59. Run against the previous code, it
failed on all three leaks.

## CSV of a grouped report: still data rows only

The round 7 critic noted again that the CSV carries no totals. That was decided earlier
(`p06-form-report-report-framework.md`, test
`Csv_carries_the_rows_only_so_another_program_reads_every_line_as_data`). A CSV is read by
another program, and a total line would be read as one more record. The totals are in the PDF,
on screen and in the XLSX (grand total and each group's total, as filter-aware formulas). A totals
line was written and then removed this round. It would have changed that test, and the bar does
not move down.

## Processor time

This round adds one gate test that shares an existing environment (3 s wall in a targeted run,
roughly 56 report prints), two vitest cases (well under 1 s) and a few steps in one existing e2e
test (a few seconds). That is an estimated 15 to 20 processor seconds. The verify's measured total
is given in the commit that records this round's run.
