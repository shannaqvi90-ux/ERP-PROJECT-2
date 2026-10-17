# p06 — Round 7: a phone field that keeps +971, and a completed entry that saves itself

Date: 2026-10-17. Piece: p06-form-report. Status: accepted.

## The gap (critic p06 round 6)

Edit and save only tied Odoo's shortest path. In Odoo an expert triple-clicks the phone field,
types `+971 50 555 0199` and clicks Save: 3 steps, 17 keystrokes (verified by the critic on the
shared rig). Ours was click, type the same 16 characters (17 keys with Shift), Enter: 3 steps and
18 keys, or 17 keys with a click on Save. A tie is a loss, and every metric must be strictly
better. Editing one value of a saved record is the commonest thing every later module does with
this form framework.

## Decision

### A phone field type in the form kernel (`PhoneField`, `web/src/kernel/forms/phone.ts`)

- The field keeps the country code, +971, and takes the number after it. It groups the digits as
  they are read aloud: mobile `50 555 0199`, fixed line `4 555 0100`, toll-free and shared-cost
  `800 1234` / `600 522222`. Nobody types the spaces.
- It takes the number however a UAE user types or pastes it: with the trunk 0 (`050 555 0199`),
  with `00971`, `+971` or `971` without the +, and in Arabic-Indic digits. A `+` starts another
  country's number, which is kept as typed (digits, spaces, brackets and dashes).
- A click into the field selects the number after +971 (the +971 is kept), so typing replaces it.
  Tab still selects the whole value, and the field reads what is typed the same way. An empty
  field shows `+971 ` while it has the focus and stays empty, and not dirty, until a digit is typed.
- The caret stays before the same digits when the field regroups them while someone edits in the
  middle of a number.
- The company and branch phone fields use it. The server's check is unchanged
  (`TenancyValidation.PhonePattern`), and what the field stores always passes it.

The lengths come from the UAE's numbering plan. The ITU-T E.164 country code is 971, and the TDRA
keeps the national plan (ITU national numbering plans: <https://www.itu.int/oth/T0202/>; checked
2026-10-17). Subscriber numbers are seven digits after a one-digit area code (2, 3, 4, 6, 7, 9),
and mobile numbers are 5 and eight more digits. This is a formatting rule, not a tax, payroll or
statutory calculation, so it is not a human gate. A number the field does not recognise is
never refused. It is simply never treated as complete.

### A completed entry saves (useRecordForm `completeEntry`, `autoSaved`, `undo`)

When a person types the last digit of a whole UAE mobile or fixed-line number, the entry is
complete. If the record is saved, the user may change it, and that field is the record's only
change, the form saves straight away, with no key to press. These cases never save on their own:

- other unsaved changes on the record (the person is in the middle of a larger edit and saves
  when done);
- a new record;
- a pasted number, or one set all at once (one more digit typed must complete it: a pasted
  number may still be under review);
- a number of another country, a toll-free or shared-cost number, or any number whose length is
  not fixed.

The saved notice then reads "Saved: the entry was complete." It has an Undo button, and Alt+Z (the
discard key, which has nothing to discard once the record is saved) undoes the save by saving
the previous values back. Enter or Ctrl+S straight after such a save sends nothing more, so no new
version and no empty audit entry are made. Any other save of an unchanged record still goes to
the server as before. The keyboard permission gate's control relies on that.

The field's hint says so in both languages ("The number after +971 (+ for another country). Its
last digit saves the record."). The dirty and leave model is unchanged: the save happens only on
completion, never on a timer or on leaving the field.

Why not autosave on leave or on a timer (Dynamics 365 style)? Either one changes what "unsaved"
means on every form, and the form tests and the leave guard depend on that meaning. A timer also
costs machine seconds. A save on a completed entry is narrow, predictable and can be undone.

### Result (harness, median of 3, shared rig, branding hidden)

| | steps | keys | machine s | modelled s |
|---|---|---|---|---|
| ours: click > type `+971505550199` | 2 | 14 | 0.135 | 6.97 |
| Odoo, harness path (click > Ctrl+A > type > Save) | 4 | 19 | 0.309 | 10.28 |
| Odoo, critic's triple-click path (by hand) | 3 | 17 | ~0.42 | ~10.3 |

A person can also type only the number after +971 (`505550199`, 9 keys, or `0505550199`). The
instrument credits an entered value only when every part of it was typed (runner round 10), so
the measured path types +971 as well. That keeps the measurement on the instrument's own rules.
The instrument's missing triple-click is p01's (the critic routed it there) and is not changed
here.

### Other round-6 findings

- **The grouping column repeated under its own heading** (branch directory by company): on screen
  and in the PDF, a grouped document leaves the grouping column out of its table, unless that
  column carries a total or is the only one. JSON, CSV and XLSX keep it, because a file's rows must
  stand on their own when sorted or filtered.
- **Group totals missing from the files:** the XLSX lists each group's total under the grand
  total, outside the filtered range. Each is a `SUBTOTAL(109, …)` over that group's rows, named
  "Total · group" in the document's language. The CSV keeps only data rows. An earlier decision,
  with its test `Csv_carries_the_rows_only_so_another_program_reads_every_line_as_data`, holds a
  CSV to rows another program reads as data, so the totals are in the XLSX, which offers the same
  report.
- **Country as a two-letter text box** (routed to p02; the company form is this piece's): the
  form kernel's `CountryField` offers the countries by name in the screen's language, with the
  UAE and the Gulf first. It keeps the ISO code, which the server checks as before. The codes are
  those the server accepts (the regions of the runtime's ICU cultures). The names come from the
  browser's CLDR data, so no country name is kept in the source. A stored code outside the list
  stays choosable.
- **The TRN on Arabic-Indic documents** (owner decision 2026-10-17 in
  `docs/compliance/vat-trn-format.md`): text values are never re-digited. A report API test now
  checks that the TRN and the phone print exactly as stored, in 0 to 9, on a document printed
  with Arabic-Indic digits.
- **PDFs stop at 2,000 rows:** unchanged. The isolation and permission gates print every list
  in every shape. A higher limit multiplies their PDF work on the 100,000-row lists, and the
  verify's processor time is near its 10,500 s maximum. CSV and XLSX still carry 200,000 rows,
  and the PDF says where it stopped.

## Gates

- G2 on the new write path: a read-only user's phone field is disabled and has no hint, and a
  whole number typed into it sends nothing (forms test). Plant **W-complete** in
  `web/scripts/forms-plant-self-test.mjs` makes the field editable, drops the permission check in
  `completeEntry` and in `save()`. The gate catches it.
- G1: no new endpoint and no new stored state. The XLSX group totals print values the same
  export already holds.
- The compare harness's plant S1 on the real ours edit-and-save driver is fitted to the new path
  (click and typed number). The plant is unchanged and the lint still catches it.

## Processor time this round adds

About 12 web unit tests (well under 2 s of test time), one keyboard-gate plant (one bailed vitest
run of the kernel gates, about 15 s of processor time), one end-to-end test (about 3 s) and three
.NET tests (under 1 s). Together that is under 30 s of the verify's processor time. Measured: this round's passing
verify used 9,218 s of processor time (dotnet 7,189, web 1,669, e2e 312, timing 48; maximum
10,500 s), on a busy machine (load up to 45), with 631 .NET, 489 web unit, 103 end-to-end, 390
compare and 6 build tests passing. The ratchet's suite minimums are raised to those counts.
