# p11 — Attachments

- Upload, download, preview and delete files on any record; drag and drop; several at once.
- Storage behind an interface (local volume in the demo); paths never derived from client
  input; per-tenant separation; size and type limits; audit.
- Files are reachable only through permission-checked endpoints. G1 attacks attachment ids
  and paths (including traversal) across tenants.

Compared against Odoo: attach a file to a record and find it again.
