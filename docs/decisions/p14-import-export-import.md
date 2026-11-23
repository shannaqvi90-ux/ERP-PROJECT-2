# p14 — Import: how a file becomes records

Date: 2026-11-18. Piece: p14-import-export, round 1. Status: accepted.

## Decision

- **One engine, many kinds of record.** A module registers a kind with `ModuleBuilder.ImportRecord` (table, label, the permission that
  creates the record, its fields with header aliases, and one delegate that validates a batch of rows and, unless it is a dry run,
  writes the valid ones). The import module owns the file, the column mapping, the report and the error file; it knows no module's tables.
  Contacts is the first kind (`ContactsImport.cs`). Custom fields of the kind are offered as fields named `custom.<key>` and typed by
  `ImportCustomValues` (kernel), so every later kind gets them without code.
- **The record's own rules judge every row.** The contacts importer builds the same `SaveContactRequest` the form and API take and runs
  the same `Validate` and `CheckAsync`, twice when a row has problems (English, then Arabic), so each problem is kept in both languages
  (a background job has no reader's language). A file can never add a contact the screen would refuse.
- **Upload, then map, then check, then import.** `POST …/imports` stores the bytes (its own tables `import_sessions`, `import_files`;
  RLS and audit like any tenant table, the audit trail records only sizes), reads the file and suggests a field per column: first what
  an earlier import chose for the same header (`import_mappings`, per workspace and kind), then the header's own name, label or alias
  (English and Arabic, digits and punctuation ignored). `PUT …/mapping` changes it and remembers it. `POST …/run` with `dryRun` only
  checks and reports; without it the file is imported. `onError` is `reject` (default: nothing is written while any row has a problem)
  or `skip` (the valid rows are written, the others stay in the error file).
- **The error file** is a CSV of the rows that had problems, as the file had them, with the row number and the problems in English and
  Arabic: fix them in place and upload it again.
- **Small files in the request, large ones in the background.** Up to 5,000 rows (`ImportRunner.InlineRows`) are processed while the
  person waits (a few seconds); larger ones are queued as the job `importexport.import` and the dialog looks at the file until it ends.
  The code is the same (`ImportRunner`). Reason: the comparison task imports exactly 5,000 rows and a job's pick-up latency (the
  worker polls every 2 s) and polling would add seconds to what the person waits for; a request of a few seconds is fine, one of
  minutes is not (rows above 5,000 can take 10 s per 5,000 with custom-field defaults).
- **Writes are bulk and atomic.** A batch of 500 valid contacts is one `COPY` into a staging table and one `INSERT … SELECT` through the
  application role (`BulkInsert`): row-level security and the audit trigger see every row, as for any write. Custom field values of the
  batch go in one `SaveNewAsync` (kernel `ICustomFields`, additive). Everything is one unit of work (the request's or the job's), so a
  failure leaves the records as they were. Writing 5,000 contacts through `SaveChanges` took 5.7 s of the 6.5 s; the bulk path 2.5 s.
- **No progress report while writing.** `IJobContext.ReportAsync` writes through a connection of its own and its update of `jobs.jobs`
  waits for the audit trail's lock that the first audited row of the unit of work takes (measured: 5.7 s per report). The job reports
  while checking and once before it starts writing, not during.
- **A failed job leaves the file "running".** The job's unit of work is rolled back on an unexpected error, together with the file's
  status. Known problems (a damaged file, an unknown kind) are written to the file's record and the job ends normally; an unexpected
  error shows on the Background jobs screen (the dialog shows the job's number in the file's record). Accepted for round 1.
- **No spreadsheet library.** CSV (comma, semicolon or tab; quotes; BOM; UTF-8, else the Windows Arabic code page) and XLSX (a zip of XML:
  the first sheet, shared and inline strings, numbers as stored, dates by their number format) are read by `TabularFile` with the
  framework's `System.IO.Compression` and `System.Xml`: no new dependency (CLAUDE.md rule 6). Bounded: 25 MB, 50,000 rows, 80 columns,
  4,000 characters a cell, 200 MB inflated per part, DTDs prohibited and no resolver (a crafted workbook cannot read a file or a URL).
- **Cells that a spreadsheet would run as formulas** (`=`, `+`, `-`, `@` followed by something that is not a plain number or phone
  number) are prefixed with an apostrophe in the error file and the template.
- **Permissions.** Every import endpoint is guarded by the permission that creates the record (`contacts.contacts.create`): whoever
  may add a contact by hand may add them from a file, nobody else; the module's own permissions are only for the full export.
  G2 reviews the map (`tests/Gates/endpoint-permissions/importexport.txt`) and the writes (`permission-write-sets.txt`,
  `ImportExportWriteSetTests`).
- **Isolation.** Four new tables with `tenant_id`, RLS enabled and forced, composite foreign keys with the tenant; the files'
  bytes live in the database (not on a volume), so no path ever reaches a file system and the isolation probe
  (`ImportExportIsolationProbe`) attacks ids and headers only. The seeder gives every gate and demo workspace a finished import with an
  error file, a remembered mapping and a finished export, carrying the workspace's canary.

## Why

The owner's goal asks for import "better than Odoo" for UAE trading firms: a person brings a spreadsheet from another system. Odoo makes
the person pick a file, check "columns matched", test, import; it refuses a file with an error unless the person fixes and re-uploads.
Ours matches the same plain headers, keeps what the person chose, reports every problem in the language of the screen, and hands back
exactly the rows to fix.

## Processor time added to ./erp verify

The new tests add about 25 s of .NET (ImportTests 16 s, TenantExportTests 4 s, ImportExportWriteSetTests 5 s) and about 3 s of web unit
tests. The gate suites grow with the 13 new endpoints (G1 attacks every route); the cost per endpoint is in
`p00-foundation-gate-cost.md`. No new heavy stage.
