# p11 — A file is as private as its record: endpoints per kind of record

Date: 2026-11-09. Piece: p11-attachments, round 1. Status: accepted.

## Decision

- A module whose records take files registers the kind: `module.AttachmentRecord(new AttachmentRecordType(table, labelKey,
  readPermission, writePermission, exists, firstIds, listPath))` (kernel, `Erp.Kernel.Attachments`). Contacts registers
  `contacts.contacts` with `contacts.contacts.read` and `contacts.contacts.update`; it is the only change in the contacts module
  besides one line in its form (`AttachmentsSection`). The same pattern as p09's `CustomFieldRecord`.
- The attachments module maps four endpoints for each registered kind at start: list (GET `/api/attachments/{schema.table}/{id}/files`, `id` the
  record), upload (POST, the same address), content (GET `/api/attachments/{schema.table}/files/{id}/content`, `id` the file, `?inline=true` for
  images and PDF) and delete (DELETE `/api/attachments/{schema.table}/files/{id}`). A file is addressed by its own id: the row knows its record,
  so there is no pairing of a record id and a file id to get wrong, and the shared gates' generic writes (a create, then a delete by id) reach them. Each one declares
  the record's own permission, so the G2 gates see ordinary, reviewed permissions: `tests/Gates/endpoint-permissions/attachments.txt`
  lists the four contacts lines. A new kind adds four lines there; nothing passes unreviewed.
- The module has no permission of its own (a dead permission fails G2). Its audit kind (`attachments.files`) is read with
  `audit.log.read`: uploads and deletes appear in the audit log for whoever reads the whole log.
- Every call first asks the kind's `Exists` (the record's own DbContext, so row-level security and, later, company scope apply): a record
  that is not there or is another workspace's answers 404 whatever else the request says, so a probe cannot tell the two apart. A
  file id is only valid under its own record (the query names record type, record id and file id).
- Reading follows the record's read permission, adding and deleting its update permission (decision of the lead's routing note).

## Not chosen

- One set of endpoints with the kind as a route parameter: the permission would be decided in the handler, invisible to the G2 map.
- A permission such as `attachments.files.read` held on top of the record's: two grants to explain, and "view contact" would not
  show its files.
