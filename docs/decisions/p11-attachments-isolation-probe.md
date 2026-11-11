# p11 — How the tenant-isolation gate reaches attachments

Date: 2026-11-09. Piece: p11-attachments, round 1. Status: accepted.

Shared gate machinery (p00's) is not edited. The gates reach attachments in three ways that already exist:

1. **G1 HTTP attack** enumerates the four endpoints of each kind from the running app and sends tenant B's ids in every route and query
   parameter; the upload takes the id of a contact, so a foreign id answers 404 like an unknown one.
2. **G1 database**: `attachments.files` is a tenant table with `tenant_id`, row-level security enabled and forced, and the audit trigger.
3. **A registered probe** (`AttachmentsIsolationProbe`, `module.IsolationProbe<>()`, kind `File`): signed in as tenant A it attaches a file to
   its own first contact (found through the kind's `ListPath`), then tries every tenant B id as record and as file under both records
   (list, content with and without `inline`, delete, upload), with tenant-switch headers, with file names and route segments that try
   to leave the folder or name another tenant's folder or key, and finally checks that its own file reads back, that tenant B's seeded file (found
   through tenant B's own list; the probe writes nothing as tenant B, whose rows the gate also watches for changes; the seeded files carry B's canary in
   name and text) is unchanged and has no planted file next to it, and that
   no stored name keeps a separator. Every answer's text, bytes and headers go back to the gate, which looks for B's ids and canaries.
   An attempt that gets no answer, or any of the checks above failing, is a `Failure`, which fails the gate.

Self-tests: `AttachmentsProbeSelfTests` plants a store that ignores the tenant, a download that trusts the file id alone, and a name
that reaches the file system, and requires the probe to catch each. Missing from the shared machinery: none for this round; if the
list-volume or partial-body generators send an upload body, they send a non-file body to a route that already refuses it (400/415).
