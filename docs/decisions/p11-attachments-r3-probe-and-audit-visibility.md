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

## Fix 1 after the integrator's alone run (processor time)

The integrator's alone run passed every test but used 21,583 processor seconds (dotnet 18,199) against 21,300 allowed. G1ListVolumeTests took
29 min 23 s there against 19 min 50 s on the integration branch, and G1HttpIsolationTests 2 h 53 min against 2 h 40 min: the audit log's
query had gained a JSON containment branch (`changes @> ...`) that the database could not answer from its indexes for the audit log at demo
volume. `AuditReader.VisibleAsync` now reads a kind with a parent as a plain kind (`record_type = ...`) when the caller may read every kind
that takes files (the usual case, and the only one today), and uses the containment test only for a caller who may read some of them. The
rule is unchanged: AttachmentTests.The_audit_log_shows_a_file_change_only_to_someone_who_may_read_its_record passes (18/18 of AttachmentTests).
Measured: G1ListVolumeTests alone 18 min 7 s (was 29 min 23 s); G1AttachmentsAttackTests, AttachmentGateTests, G1TenantSourceTests and
AuditGateTests pass. Not run by the fixer (owner decision #41): G1HttpIsolationTests, the full verify. Expected saving: about the 10 to 13
minutes of wall time those two tests gained, which should bring processor time back under 21,300 s.
