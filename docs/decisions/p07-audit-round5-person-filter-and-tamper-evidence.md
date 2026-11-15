# p07 round 5: a person picker for Changed by, a trail only the triggers write, names in the Fields column

Date: 2026-11-15. Piece: p07-audit, round 5 (answers the critic of fadf329d). Status: accepted.

## Changed by (the round 4 critic's biggest gap)

The column is a reference to a person; the filter was a text box, so a name became `actorId eq 'Noor Editor'` and the
server answered 400. Now `ReferenceSource.search` (web kernel, additive) turns a reference column's filter into a picker:
a combobox (type, Arrow keys, Enter chooses, Enter again applies; chosen people stay as checkboxes), keyboard and mouse,
both languages. The audit module serves it with `GET /api/audit/actors?search=&take=` (permission audit.log.read), which
reads identity's public `IUserDirectory.SearchAsync` (now also searching the Arabic name). A reader who may not read users
gets names only: no e-mail in the answer, and an e-mail typed finds nobody for them. The filter only ever sends the chosen
person's id, so it cannot answer 400 or leave the list unfiltered. The chip names the person.
Why a new endpoint and not `/api/identity/users`: an auditor needs this without the permission to read users.

## Tamper evidence (CLAUDE.md rule 4)

`erp_app` held INSERT on `audit.entries`, so the application role could write a made-up row (any actor, any time).
Considered: a RESTRICTIVE insert policy on `pg_trigger_depth() > 0`. Rejected: the application role may create temporary
tables (BulkInsert needs it) and temporary functions, so it could attach its own trigger and pass the check. Chosen
(the design of round 3, which was backed out because the custom-fields history trigger also writes the trail): both
writers become SECURITY DEFINER trigger functions owned by the NOLOGIN role `erp_auth`, with a pinned `search_path`:
`audit.capture()` (kernel migration `AuditTamperEvidence`) and `customfields.audit_record_history()` (custom-fields
migration `AuditHistoryDefiner`, after the kernel's). INSERT is revoked from `erp_app`; a policy lets only `erp_auth`
insert. Time (`clock_timestamp()`), actor and correlation id still come from the transaction, as before. A trigger
function cannot be called as a statement and the application role holds no EXECUTE on it. The migrations' role is granted
EXECUTE so a table's migration can still attach the trigger.
Gates: `security-definer-trigger-only.txt` lists the two functions; G1ReviewedLookupTests checks they are trigger
functions nobody application-side can execute; the definer count is split in the ratchet (lookups stay at 2; new key
`g1.securityDefinerTriggerFunctions` at 2); G1DatabaseIsolationTests refuses an INSERT by the application role and allows
only the reviewed `audit_trigger_insert` policy for `erp_auth`. AuditTrailTests has a database test that the application
role cannot insert, update or delete an audit row while the trigger still writes.
Limit that remains and is inherent: the application sets the actor of a change it really makes (`app.actor_id`).

## Names, not counts (Fields column and export)

`AuditLogRow.FieldNames` (the fields in the request's language, a custom field by the tenant's label, each once); the
column's `LabelField` names it; the screen renders it and the report engine joins it for print, CSV, XLSX and PDF.

## Times and words in the change text

An ISO instant inside a value is written as a date and time in the language (Gulf time, UTC+4, the product's default
workspace zone: `14 Nov 2026, 11:37 PM`; Arabic `14/11/2026، 11:37 م` as the reports print it). The Arabic "set" text no
longer says "القيمة" (value) before the value; it says "الحقل" (field), as the string gate requires Arabic letters in
each text. dd/mm/yyyy in English belongs to p18's design system; the shared date format is untouched.

## Fields filter

Fields that read the same (a company's Name and a contact's Name) are one entry of the filter that takes every field of
that name (`fields in (...)`), and its chip names the word once.

## One save, one line

A save that changes a record's own field and a custom field writes one row per table. The line under the heading now
merges the newest row with the same person's update rows within two seconds (the heading reads 6 rows, not 1).
Not done: one row per save in the database (it would change the capture contract p09 relies on).

## Cost

Processor time added: negligible at runtime (one definer call per audit row as before; the picker reads at most 20 users
per keystroke). Tests added: 4 module tests, 3 web tests, 1 e2e spec. No new package.

## Integration fix (round 5, after the integrator's alone verify)

The alone verify failed in `CustomFieldGateTests` (customfields.audit_record_history, security definer): the
custom-field gate lets no function of the `customfields` schema run as its owner. The gate is unchanged. The
history trigger of a custom field value now lives in the audit schema as `audit.custom_field_history()`, a
trigger-only definer function owned by erp_auth (listed in security-definer-allowlist.txt and
security-definer-trigger-only.txt, like audit.capture()); `customfields.audit_record_history()` is replaced by
the caller-rights, pure `customfields.history_change()`, so the schema keeps its 12 functions and none is a
definer. Migration 20261116000100_AuditHistoryDefiner was rewritten in place (it was never integrated); its
Down is not supported (the writers do not go back to the application role).

Targeted runs (toolbox container, own containers removed): CustomFieldGateTests, G1ReviewedLookupTests,
G1DatabaseIsolationTests, G1TenantSourceTests, AuditGateTests and the other Rules gates, 69 passed; Audit module
tests 20 passed; CustomFields module tests 28 passed. The full verify was not run (the integrator's alone run).
Processor time of this fix: a few hundred seconds; it adds none to the full verify. The 22,612 s total of the
failed alone run was over the 21,300 s limit; this fix does not reduce it.
