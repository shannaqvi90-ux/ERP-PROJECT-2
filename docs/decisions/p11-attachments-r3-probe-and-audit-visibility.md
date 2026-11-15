# p11-attachments round 3: the probe's victim, and who sees a file's audit rows

Date: 2026-11-15. Status: accepted.

## 1. The probe finds tenant B's files by name, not by the first page of a list

Problem: the probe took the victim's seeded file from the first record of tenant B's list page that had any. The seeder puts files on the
first contacts by name, archived ones included, and tenant B's concurrent creates, archives and restores (the gate's own victim activity)
pushed them off that page or archived them, so the probe answered 'the victim has no file to defend' and failed G1AttachmentsAttackTests
and G1HttpIsolationTests in other pieces' runs.

Decision: the probe asks tenant B's own file search (`GET /api/attachments/<kind>/files?search=...`, the command palette's search) for the
seeded names ("Trade licence", "Delivery note"). It is a direct lookup by name over all of tenant B's files, independent of list order,
paging and archive state; it reads the first match that still serves its bytes. When no file is found the answer is still a failure (the
attack is never skipped). Rejected: attaching a file as tenant B from the probe. The gate watches tenant B's rows for changes during the
attack and the audit rows of that upload and delete failed it ('Tenant B rows changed during the attack in: audit.entries'), so the probe
writes nothing as the victim. Self-tests: a file on a later record than the list's first, and a victim without any file (failure).

## 2. A file's audit rows are as private as the record they belong to

Problem: `attachments.files` was registered with `audit.log.read`, so a role holding only that and `identity.roles.read` saw the names and
checksums of contacts' files in the log.

Decision: for an audited kind that names a parent record (`ParentKindField`), `AuditReader` ignores the kind's own `ReadPermission` and shows
a row only when the caller holds the read permission of the parent kind (`AttachmentRecordType.ReadPermission`) named in the row, as added or as
removed (JSON containment on the row's changes, served by the existing GIN index). The same rule covers the log, its filters, print, export
and the record history, since all go through `VisibleAsync`. Limits: file rows carry no company, so company scoping of the parent is not applied
(contacts are tenant-wide). Test: `AttachmentTests.The_audit_log_shows_a_file_change_only_to_someone_who_may_read_its_record`.

## Cost

No new endpoint. The probe adds two GET searches per kind; the audit visibility adds one OR branch per readable parent kind to the audit
query (one kind today). Gate processor time added: negligible (below 20 s).
