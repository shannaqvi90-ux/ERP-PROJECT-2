# p06 — Gates for printing and exporting

Date: 2026-10-04. Piece: p06-form-report. Status: accepted.

## Decision

Printing and exporting open new ways to leak another tenant's data: inside a PDF's compressed
streams, inside an XLSX's zipped XML, in a print time that differs between two answers. The gates
became stricter before the reports module was written:

- **G1 reads what a file says.** `ResponseText` decodes every answer before the gate judges it:
  a PDF's page text (Arabic in logical order, `/ActualText`, inflated streams), every entry of a
  ZIP (XLSX) with XML entities decoded, CSV and JSON as text. A tenant B canary inside a PDF or a
  spreadsheet is a leak like one in JSON. Self-test plants (`LeakyModule`: `/export.pdf` and
  `/export.xlsx` from a process-wide cache) prove the gate catches both.
- **Print stamps are declared, not guessed.** A printed answer carries
  `X-Erp-Printed-At: <ISO instant>;<printed text>`; the gate replaces exactly that text (within two
  days of now, at most 48 characters, at least four digits) with `<printed-at>` before comparing two
  answers, and repeats a differential comparison once when either answer is stamped (the second
  answer is the one judged). Nothing else is scrubbed, so a leak cannot hide in a header.
- **G2 derives each report route's permission** from the report's (or list's) registration and
  compares it with the reviewed map `tests/Gates/endpoint-permissions/reports.txt`.
- **Fonts are licence-reviewed files.** Every `.ttf/.otf/.woff/.woff2` in the repository must be
  listed in `tests/Gates/font-licences.txt` with its SHA-256, its licence (OFL-1.1, owner approval
  needs-human #5) and the licence text that sits beside it; a changed byte fails.
- **Every report renders.** `ReportGateTests` runs every report and every printable list in
  json, pdf, csv and xlsx, in English and Arabic (64 renders today), checks definitions are well
  formed, and requires every list to be printable or listed in `tests/Gates/unprintable-lists.txt`.
- **Report labels exist in both languages** (string gate).

Ratchet minimums: `rules.fontFilesChecked` 6, `rules.reportsChecked` 3,
`rules.printableListsChecked` 5, `rules.reportRenders` 64.

## Differential control for enumerated parameters (2026-10-04)

The G1 HTTP attack compares each tenant B value's answer with a value that exists nowhere. For a
parameter whose values the API document enumerates (a report's choice parameter such as
`emirate`), a random value is refused by validation (400) while tenant B's value is valid (200), so
the old pair always differed and said nothing about existence. The control for such a parameter is
now another enumerated value that no tenant holds in any text column (read with the superuser);
the random control is kept for every other parameter and when no unheld member exists. Every
value is still sent and every answer still judged for tenant B's markers.
