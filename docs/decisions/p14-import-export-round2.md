# p14 round 2: the export that cannot fail silently, duplicates on import, and the filtered-list export

Round 1's critic (verdict r001, commit d9d6f5e9) found the full tenant export crashing on the shipped stack and blocking every later export, the filtered-list export tying Odoo on keystrokes, and a list of import gaps. This record gives what was chosen and why.

## The worker needs the attachment volume

`compose.yaml` gave the `attachments-data` volume and `Erp__Attachments__Root` to `setup` and `app` only. The export reads attachment bytes in the job, which runs in the `worker`; the store tried to create `/app/attachments-data` and failed with `UnauthorizedAccessException`. The worker now has the same volume and setting as the app. Reason: one store, one volume, the bytes the app wrote are the bytes the worker reads.

## A failed export ends visibly and never blocks

- The job handler catches every error (not only "too large"): storage errors become `exportStorage`, anything else `exportFailed`, a stop becomes `exportStopped`. The export record is saved as `failed` with its reason in English and Arabic and the handler returns, so the unit of work that saves the failure is not rolled back by a thrown error. The error itself is logged.
- An export still queued or running five hours after it was made (the job's own limit is four) is given up as `failed` / `exportStalled`, both when the list is read (shown as failed) and when a new export is started (saved as failed first). This covers a lost worker or a job that died without reaching the handler. Reason: one-at-a-time must never become forever.
- A failed row on the screen shows its reason and a "Try again" button that starts a new export with the same format. The start button was never disabled by a failed export. No database edit is needed.
- Tests: `TenantExportFailureTests` (storage turned into a file so the store cannot make its folder, the same crash as the worker's; the aged-queued case), `ExportsPage.test.tsx` (reason and retry), and the e2e now waits for the export it started (the new first row, not the seeded finished one), downloads it and checks the zip's file names.

## Every module's records are in the full export

`Erp.Kernel.ImportExport.ExportEntities.For<TContext, TEntity>(table, labelKey, exclude...)` writes every plain column of an entity (text, numbers, amounts with their currency, rate and base amount columns, dates, flags, ids, lists as JSON) in id order through the module's own context, so row-level security and filters apply as for any read. It never writes binary columns (device approval hashes), the row version or the workspace id. Registered by their modules: currencies and exchange rates, companies, branches, user company and branch access, users, roles, user roles, user company roles, custom field definitions (values are already columns of their records), numbering sequences, saved list views. Credentials, sessions, passkeys and sign-in attempts are left out on purpose: they are secrets, not business data, and an export file is a document a person keeps. The audit trail is not in the file: it is large (hundreds of thousands of rows per workspace in the demo), is read through the audit screen's own CSV and XLSX export with its permission, and writing it needs a streamed zip; recorded as the open part of needs-human #44 below.

Open: the whole zip is still built in memory and stored in the database, capped at 256 MB. A streamed zip kept in the attachment store, and the audit trail in the file, are the next step; neither was done in this round.

## Isolation probe

`ImportExportIsolationProbe` now has tenant B download its own finished export (its administrator's write) before tenant A attacks with B's export ids, so a cache or a file kept between requests holds B's file when A arrives. The probe reports a failure if B's own download is refused.

## Import: duplicates, rates, error file, plurals

- A check and an import count the rows that match a contact already there (same reference, tax registration number, e-mail, phone number or name, the same five things the contact form warns of), read once per batch of 500 rows, not once per row. `rows_duplicate`, `rows_updated` and `on_duplicate` are new columns of `import_sessions` (migration `DuplicateHandling`; audit field names in both languages).
- The run takes `onDuplicate`: `create` (default, add anyway; the check warns), `skip` (leave those rows out) or `update` (change the contact already there). An update takes only the columns the file has and only non-blank cells; the contact is found by reference, else tax number, else e-mail, else name, else phone, and a row that matches more than one contact at the best level is refused as ambiguous. Updates are saved through the context so the audit trail records what changed. Reason: this is what Odoo's import offers (update by an identifying column) and what a person importing a corrected file needs.
- A foreign-currency credit limit with no rate in the file takes the workspace's own latest rate within the currency's age limit (`IExchangeRates`, a public contract of the Currency module); with none, the row is refused with the old message.
- The error file names a problem's column by the file's own header, and in Arabic by the field's Arabic label; never by the field's key.
- Counts use plural messages with all six Arabic forms (zero, one, two, few, many, other): checked rows, valid rows, rows with problems, duplicates, updated records, files in an export.

## Export a filtered list: the tag is a button

Round 1: Contacts > Tags column options > Filter... > click the value > type the tag > Apply > Print or export > Excel: 8 steps, 11 keystrokes, tied Odoo (11) on keystrokes. Paths tried and why they do not win by themselves: the list search (does not look at tags), Tab or Enter instead of a click (one more key each), a cell shortcut (filters on the whole cell text, so contacts with more than one tag are missed), a lower-case tag (the Odoo driver types the tag as given, so inputs must stay equal). Chosen: a contact's tags are drawn as buttons in the list; a click lists the contacts whose tags contain it (`FilterByWordContext`, a small additive extension point of the list, with the buttons out of the Tab order so the grid's keys are unchanged). Path: Contacts > click the tag > Print or export > Excel in English: 4 steps and 0 keystrokes against Odoo's 9 and 11. The ours driver is in `gauntlet/compare/drivers/ours/export-filtered-list.mjs`.

## Cost

No new dependency. The duplicate read adds one query per 500-row batch in each of the check and write passes of an import. The full export adds nine small tables. Processor time of the verify is expected to rise by a few hundred seconds at most (more tests and one more gate surface); measured by the integrator's alone run.
