# p11 — Where attachment bytes live, and why no client text reaches the file system

Date: 2026-11-09. Piece: p11-attachments, round 1. Status: accepted.

## Decision

- The bytes sit behind `IAttachmentStore` (`Erp.Modules.Attachments.Contracts`): `SaveAsync(tenant, stream, maxBytes)`,
  `OpenAsync(tenant, key)`, `DeleteAsync(tenant, key)`. The product registers `LocalAttachmentStore` with
  `TryAddSingleton`, so a host that registers another store first (object storage) replaces it without touching an endpoint.
- `LocalAttachmentStore` keeps `{root}/{tenant:N}/{id:N}`: one folder per tenant, one file per attachment. The root is
  `Erp:Attachments:Root`, by default `attachments-data` beside the application. In the compose stack it is the volume
  `attachments-data` mounted at `/data/attachments` in `setup` and `app`; `./erp verify` and the in-process tests use the default.
- Nothing from the client makes or finds a path. The folder is the session's tenant id (the endpoint takes it from
  `ITenantContext`), the file name is a fresh `Guid` made by the store. The stored key is `{tenant:N}/{id:N}`; `OpenAsync` and
  `DeleteAsync` accept only a key that matches `^[0-9a-f]{32}/[0-9a-f]{32}\z` and whose first half is the call's tenant, and build
  the path from the two parsed halves. The client's file name is only text in the tenant's own row (cleaned: last path part only,
  control, format and separator characters replaced, shortened to 150 characters with the extension kept) and the download's name.
- The database row refuses a key of another tenant (`ck_files_storage_key`: pattern and `left(storage_key, 32) = tenant_id` as hex),
  so even a superuser's insert cannot point tenant A's row at tenant B's bytes.
- A write goes to `{id}.part` (`FileMode.CreateNew`, `FileShare.None`) and is moved when whole; an upload over the limit, or one
  whose stream fails, leaves no file. An upload whose request ends in an error also removes its bytes (`Response.OnCompleted` checks
  the status after the unit of work has committed or rolled back); a delete removes the bytes after its commit. A crash between the
  two can leave an unreferenced file but never a row without bytes written before it.

## Reasons

- Rule 1 (tenant isolation) wants separation in storage as well as in rows: the folder per tenant, the key check in the store and the
  database check are three independent layers.
- A traversal needs a client string in a path. There is none to find: the compiled-code gate (G1OutsideProcessStateTests) lists the four
  methods that touch the file system with the reason each is safe (`tests/Gates/outside-process-methods.txt`).

## Not chosen

- Bytes in PostgreSQL (`bytea`, as company logos): simple and transactional, but 20 MB rows bloat backups, the audit trigger and the
  buffer cache. The spec asks for a storage interface and a volume.
- A client-supplied extension or name in the stored path: the usual source of traversal and of served-as-script bugs.
