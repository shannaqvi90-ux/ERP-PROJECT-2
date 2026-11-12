# p14 — Import and export

- Import CSV and XLSX into any registered record type: column mapping with memory of past
  mappings, preview, validation report before commit, dry run, background processing for
  large files, downloadable error file, custom fields included.
- Export any list as CSV or XLSX with its current filters and columns.
- Import and export files are tenant-isolated (G1 attacks export ids and file paths) and
  permissioned (G2).
- Full tenant export (owner decision 2026-11-12, needs-human #44): a tenant's administrator (and
  the operator while a tenant is being closed) can export all of the tenant's data in open,
  documented formats (CSV or JSON per record type, with its attachments and a manifest), as a
  background job with progress; the download is permission-checked, expires, and is audited, and
  G1 attacks it (one tenant's export never holds another's rows or files).

Compared against Odoo: import 5,000 rows (contacts once p16 exists); export a filtered list.
