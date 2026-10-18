# p06 — Round 9: a printed document holds the whole selection

Date: 2026-10-19. Piece: p06-form-report. Status: accepted.

## The gap (critic p06 round 8)

Every PDF, on-screen document and print stopped at the first 2,000 rows
(`ReportEngine.DocumentRowLimit`). The document said so ("The first 2,000 of 100,007 rows; narrow
the parameters to print the rest") but gave no way to print the rest. Only CSV and XLSX carried up
to 200,000 rows. A stock list, a price list or an aged list for an auditor or a free-zone authority
often runs past 2,000 lines and is wanted as one PDF in English or Arabic. Every later module
inherits this limit through the report framework.

## Decision

### Two limits: the screen's and a file's (`ReportLimits`)

- **Screen (JSON)**: at most 2,000 rows (`Erp:Reports:ScreenRows`). A browser draws the document
  as one table. More rows than that make the screen slow and add nothing a reader can scan.
- **Files (PDF, CSV, XLSX)**: the whole selection, up to 200,000 rows (`Erp:Reports:FileRows`).
  That is twice the owner's main-list volume. The PDF now has the same limit as the exports.
- Both come from configuration. The host refuses to start if the screen limit is below 1, the file
  limit is below the screen's, or the file limit is above 1,000,000. The tests use small values
  (5 and 7) to prove both behaviours without seeding thousands of rows.

### What the document says

- A screen document that stops at its limit while its files hold every row says so:
  "The first 2,000 of 100,007 rows are shown here; the PDF, Excel and CSV hold all 100,007."
  (Arabic: "تُعرض هنا أول … من أصل … صفًا؛ وملف PDF ومصنف Excel وملف CSV تضم الصفوف كلها (…)").
  The JSON carries `filesHoldAll: true`.
- A file that stops at its own limit says what it said before: "The first 200,000 of 250,000
  rows; narrow the parameters to print the rest." The CSV puts this in its last line and the XLSX
  under the table, as before.
- A whole PDF prints its row count ("100,007 rows") under the parameters, as every document does.

### Printing from the screen

When a screen document stops at its limit and its files hold every row, the report screen
replaces **Print** (which prints the screen) with **Print all rows (PDF)**. That is a link that
opens the whole PDF inline in a new tab, where the browser's viewer prints it. **Ctrl+P** does the
same on that screen, so the keyboard never prints only the first 2,000 rows. A document that holds
every row on screen still prints from the screen with Print and Ctrl+P, as before. A list's "Print
or export" menu already printed through the PDF, which now holds the whole list.

### A PDF written page by page (`PdfWriter`, `PdfReportRenderer`)

Holding 100,000 rows of PDF drawing in memory took about 1.4 GB per request for an Arabic list
(measured below). So the writer now streams:

- Each page's drawing is compressed and written to the output as soon as the next page starts. A
  document never holds more than one page of drawing operations.
- What is drawn on a page after that (its footer, "Page x of y", which needs the page count) goes
  into a second, small content stream for that page. The page objects, the font subsets, which
  need every glyph drawn, and the cross-reference table follow at the end. The output does not
  need to be seekable.
- The endpoint renders into a scratch file in the system's temp folder under a random name, and
  then streams that file as the response. On Linux (the product's container) the file is unlinked
  as soon as it is opened, so only the request's own handle reaches the bytes and nothing is left
  behind if the process stops mid-print. On Windows it is a delete-on-close file. Its memory does
  not grow with the document, and a failed render never sends half a PDF.
- The render checks the request's cancellation token at every row, so a print the client abandons
  stops.

### Faster layout (`TextShaper`)

- A cell value that fits its column is shaped once as a whole line. Before, it was shaped once
  per word while the line was built up. The result is the same line. A value that starts with a
  space, or is longer than any line can hold (600 characters), still goes through the
  word-by-word loop.
- Shaped lines kept for reuse are capped at 20,000 per document. Past that, the store starts
  again. Before, it grew with every distinct value.
- Run and line widths are summed once, not on every call. Glyph ids are written as hex straight
  into the page content, with no string per glyph.

Measured on this machine (Release build, load average 60 to 70, so these are upper bounds), five
columns, one row per line:

| rows | before: time / peak memory | after: time / peak memory |
|---|---|---|
| 2,000 English | 0.50 s / 83 MB | 0.35 s / 78 MB |
| 100,000 English | 10.7 s / 867 MB | 6.5 s / 368 MB |
| 100,000 Arabic | 14.7 s / 1,453 MB | 9.8 s / 372 MB |

"Before" is the old renderer given 100,000 rows. The product itself never printed that many. Most
of the remaining peak memory is the document's own rows, which the CSV and XLSX exports already
held for 200,000 rows. Compression level (Optimal against Fastest) made no measurable difference
in time, so Optimal stays because its files are smaller.

### Considered and not taken

- **A background job that renders large documents and notifies the user.** Background jobs are
  p12's piece and not yet on the integration branch. A 100,000-row Arabic PDF renders in about 10 s
  on a loaded machine, well inside an HTTP request. When p12 lands, a document over a size
  threshold can move to a job without changing the document, the renderer or the limits.
- **A paged on-screen document** (2,000 rows at a time with "next"). This prints the screen,
  not the selection. The PDF is the printable form, and it now holds every row.
- **Streaming straight into the response body.** This writes no scratch file, but an error
  partway through would send a broken PDF with a 200 status. The scratch file costs one disk write.

## Other round-8 findings closed

- **A company's profile printed under the working company's letterhead.** A report about one
  company (`ReportDefinition.CompanyParameter`) is now issued under that company's legal name, in
  the document's language: the Arabic legal name when the company has one, otherwise the English
  one (docs/compliance/arabic-legal-name-on-documents.md). This applies only when the report found
  the company and the caller reads companies. Otherwise the working company's letterhead stays, as
  before. Test: `A_company_profile_is_issued_under_the_company_it_describes` (every company of the
  tenant, in English and Arabic).
- **Malformed UAE numbers were stored** (9715055501 became the "Fujairah" number +971 9 715
  055501). The server now refuses, on the phone field and in the caller's language, a number with
  the UAE's code (+971 or 00971) whose national number does not have the length the numbering plan
  gives its kind:
  - 8 digits for a fixed line (area 2, 3, 4, 6, 7, 9);
  - 9 for a mobile number (5…) or a service number (200, 300, 400, 500, 600, 700, 900);
  - 5 to 12 for an 800 toll-free number;
  - a national number starting 0 or 1 is no UAE number.

  Another country's number, or one written without its code, is not judged. Source: ITU-T E.164
  national numbering plan for country code 971 (communication of the UAE's Telecommunications
  Regulatory Authority of 9 June 2009, Table 8.2/E.129), as published by the ITU:
  <https://www.itu.int/dms_pub/itu-t/oth/02/02/T02020000DC0001PDFE.pdf> (index:
  <https://www.itu.int/oth/T02020000DC/en>), checked 2026-10-19. The plan says geographic numbers
  2, 3, 4, 6, 7, 9 are 8 digits, mobile numbers 50, 52, 55, 56 are 9 (later mobile codes such as
  54 and 58 follow the same form), service numbers 200 to 900 are 9, and 800 numbers are 5 to 12.
  This is a telephone numbering format, not a tax, payroll or statutory calculation, so it is not
  a human gate. The check lives where the phone is validated
  (`TenancyValidation.UaeNumberLengthFits`) and is reused by any later address. The field on
  screen is unchanged. A completed entry still saves on its own only for a whole mobile or fixed
  number, and the server's message now appears under the field for anything else.

## Edit and save (tie accepted in round 8)

Nothing changed here this round. p01's fixed-prefix credit (484b82bd) is still not on the
integration branch, so the 9-key national entry cannot be credited yet, and the paths table in
`p06-form-report-edit-and-save-keys.md` still holds. Ours: 2 steps and 12 keys. Odoo: 3 steps
and 12 keys.

## Gates

- **G1, the print file path** (new): `G1PrintFileTests`. Signed in as tenant A and as tenant B,
  every printable list and report in the catalogue prints as a PDF in English and Arabic (36
  prints). A watcher notes every print file seen in the scratch folder while the prints run. No
  print file may still be there afterwards, or half a second after it was seen. Ratchet
  `g1.printFilesChecked` is 36. Plant check: rendering into a kept file (no unlink, no
  delete-on-close) failed the gate with one "outlived its request" line per print.
- The HTTP isolation attack already prints every list and report in every format and both
  languages for both tenants. With the file limit now applying to PDFs, its PDFs are whole
  documents of the gate's small workspaces.
- The process-state gate still passes. `ReportLimits` is an immutable record held as a singleton
  (16 singleton services inspected).
- G2: no new endpoint or permission. Print all rows and Ctrl+P open the same report endpoint
  under the report's own permission.

## Processor time this round adds

New tests:

- four rendering tests (one renders and parses a 2,500-row PDF);
- two report API tests (two extra app hosts with small limits);
- one gate test (36 small PDFs on the shared gate environment);
- 21 tenancy cases (one HTTP request; the rest are pure);
- one web unit test;
- one end-to-end test (renders users by role, about 2,000 rows, on screen and as a PDF).

Estimated at 25 to 35 processor seconds in all. Against that, every PDF the gates print now lays
out each cell once, not once per word. Those gates print several thousand PDFs per verify. The
measured total is in the round's notes.
