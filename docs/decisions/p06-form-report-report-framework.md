# p06 — One report framework: definitions in modules, rendering in the reports module

Date: 2026-10-04. Piece: p06-form-report. Status: accepted.

## Decision

A module declares a report in its registration and supplies only its rows:

```csharp
module.Report<BranchDirectoryReport>(new ReportDefinition(
    Key: "tenancy.branchDirectory", LabelKey: "tenancy.report.branchDirectory.title",
    Permission: TenancyPermissions.BranchesRead,
    Parameters: [ new ReportParameter("company", "…", ReportParameterType.Reference, Lookup: "tenancy.companies") ],
    Columns: [ new ReportColumn("company", "…", ReportColumnType.Text, Groupable: true), … ],
    DefaultGroupBy: "company"));
```

`IReportSource.ReadAsync(ReportRun, ct)` returns `ReportData` (rows, a record document's facts and
subject, whether it was cut short and how many rows matched). Everything else is the reports
module's job, the same for every report:

| Concern | How |
|---|---|
| Parameters | Typed (`text`, `date`, `boolean`, `choice`, `reference` to a registered list for its lookup). Validated before the source runs; problems are 400 validation answers naming the parameter, in English and Arabic. Six names are reserved for the framework: `format`, `language`, `numerals`, `timeZone`, `groupBy`, `disposition`. |
| Grouping and totals | Any column marked groupable; group counts ("3 records", Arabic plural forms), group subtotals and the grand total of every column marked `Total`. Money totals per currency, never summed across currencies. |
| Formatting | Numbers, money (with its currency), dates and date-times in the document's language, Latin or Arabic-Indic digits (`numerals`), date-times in the tenant's time zone unless `timeZone` says otherwise. Values stay `decimal` end to end. |
| Formats | `json` (the screen draws it), `pdf` (A4, portrait or landscape by width, English or Arabic laid out right to left), `csv` (UTF-8 with BOM, formula-looking cells defused), `xlsx` (number cells with formats, right-to-left sheet for Arabic). |
| Limits | A document (json, pdf) holds at most 2,000 rows, an export (csv, xlsx) 20,000; beyond that it says it was cut short and how many rows matched. At most 30 columns. |
| Who and when | Every document names its issuer (the working company, else the workspace), who printed it and when; the answer carries `X-Erp-Printed-At: <ISO instant>;<printed text>` so a reader (and the G1 gate) knows which bytes are the print time. |
| What a document repeats | Only what it found. A reference parameter prints the record's name, or "Not found" for an id the caller cannot see (never the id). The search and filter values a caller typed print only when the list found rows for them, and a report's text parameters only when it found something. Raw parameter values are not sent back. An answer is the same for another workspace's id or text as for one that exists nowhere, and the isolation gate's attack phases (which do not treat a tenant B canary as "sent") can tell a leak from an echo. |
| Permission | The report's own permission (the read permission of the data it shows); the catalogue lists only what the caller may run. Reads run in a read-only transaction inside the caller's tenant and company scope. |

Lists are printable without writing a report: a module registers a row reader for each list
(`module.ListRows(listKey, PageAsync)`, the same page function its list endpoint uses), and
`/api/reports/lists/{listKey}` prints the rows the list's query (search, filter, sort, grouping,
chosen columns) selects, under the list's own permission, paging through the reader 200 rows at a
time. Every registered list must be printable or be named, with a reason, in
`tests/Gates/unprintable-lists.txt` (empty today): a list nobody can print is a gap to see.

Endpoints: `GET /api/reports/catalog`, `GET /api/reports/run/{report}` and
`GET /api/reports/lists/{list}`, each route registered per report and per list so OpenAPI documents
its exact parameters, formats and media types, and so G1 and G2 see each one.

The first reports: the company profile (a record document, printed from the company form), the
branch directory grouped by company, users by role (with status, user language and signed-in-since
parameters), and every list of identity and tenancy (users, roles, companies, branches, access).

## Why

Every later module (sales, purchases, inventory, accounting, payroll) needs printed documents,
listings with totals and exports in both languages. Doing the formatting, Arabic layout, paging,
limits and permission once, behind a contract a module fills with rows, keeps them identical and
keeps each module's code to its own data. A list's print reuses the list's own query, so what is
printed is exactly what the user filtered, and the tenant and company filtering stay where they
already are (the module's DbContext and row-level security); the reports module never sees a
tenant id.

Labels come from the web app's string files (see `p06-form-report-web-strings-on-server.md`), so a
printed column says what the screen says.

## Alternatives rejected

- A report designer or template language (Odoo's QWeb reports). Templates per report per
  language double the work and drift; a declared table with facts covers listings and record
  documents now. Invoices with tax lines will add a document layout to the same renderer when
  the sales piece needs it (they need the FTA's tax invoice fields, a human gate).
- Rendering PDFs in a headless browser (print the screen). Needs Chromium in the deployable
  (hundreds of MB, a process per print) and leaves page breaks, repeated headers and page
  numbers to CSS support; see the PDF decision.
