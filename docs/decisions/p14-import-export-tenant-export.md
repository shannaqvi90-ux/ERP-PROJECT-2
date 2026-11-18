# p14 — Full tenant export

Date: 2026-11-18. Piece: p14-import-export, round 1. Status: accepted. Source: owner decision 2026-11-12 (needs-human #44).

## Decision

- **What it is.** The workspace's administrator (permission `importexport.exports.create`; reading and downloading are
  `importexport.exports.read` and `importexport.exports.download`, three separate permissions, because the file holds all the data)
  starts the export; it runs as the background job `importexport.tenantExport` in the workspace's own unit of work (so row-level
  security limits every read to the workspace); the screen (`/importexport/exports`, G then E, Alt+N to start) shows the state and the
  download. One export is written at a time: asking while one is queued or running answers that one.
- **Format.** One zip file: `manifest.json` (format name, version, time, for each file its path, kind of record, label in both
  languages, record count, column names and SHA-256; the attachments' folder and count), `README.txt` (English and Arabic),
  `data/<schema.table>.csv|json` per kind of record that a module registered with `ModuleBuilder.ExportRecord` (the tenant's custom
  fields as extra `custom.<key>` columns), `data/attachments.csv|json` and `attachments/<id>-<name>` with the bytes. CSV is UTF-8 with a
  byte-order mark, comma separated, ISO 8601 dates, plain decimals; JSON is one array of objects keyed by the column names. Money keeps
  its currency, exchange rate and base amount in neighbouring columns because the tables do.
- **Contacts and attachments are the first kinds.** Contacts registers all its columns; attachments are read through the new public
  contract `IAttachmentExport` (list a workspace's files by id, open one), never the attachments tables. Modules added later register
  their own kinds in one line.
- **Stored in the database, kept 7 days.** The zip is written in memory (at most 256 MB, `ImportLimits.MaxExportBytes`; a larger
  workspace fails with a message that asks the operator for a staged export) and kept in `export_files` (RLS, audit records only the
  size). Reasons: no file system path and no volume to isolate, backups and tenant deletion cover it, and the isolation gate reaches it
  with the same attacks as any record. After seven days the next export (or a download attempt) refuses it (410, `expired`) and the next
  start removes the bytes. A streaming, volume-backed store is the next step when a workspace outgrows 256 MB.
- **Downloads are audited.** The download is a `POST` that counts on the export's record (`download_count`, `downloaded_at`,
  `downloaded_by`), so the audit trail names who downloaded and when. The write-set gate judges it (`ImportExportWriteSetTests`).
- **The platform operator** (p17, tenant closing) starts the same export through `ITenantExports.StartAsync` of the public contract
  `Erp.Modules.ImportExport.Contracts`, which checks no permission (the caller has); p17 owns who may call it and the closing flow.
  Nothing in this piece takes an action outside the repository.
- **G1.** `ImportExportIsolationProbe` reads tenant B's own exports and files as B, then, as tenant A, tries every tenant B id against
  every export and import action, with and without switch headers, and uploads a file naming B's ids; the answers go to the gate, which
  looks for B's ids and canaries.

## Why

The owner asked that a tenant can leave with its data in open, documented formats. A zip with a manifest and CSV or JSON files is what
an accountant or another system can read without our software.
