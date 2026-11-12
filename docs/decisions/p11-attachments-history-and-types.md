# p11 — Uploads and deletes in the record's history; seeded files show who added them; zip and mail accepted

Date: 2026-11-12. Piece: p11-attachments, round 2. Status: accepted.

## The record's History panel lists its files' uploads and deletes

`attachments.files` is registered as an audited record with an owner column (`OwnerField: "record_id"`, the audit module's existing public
contract, used by company access for users). `GET /api/audit/records/{contactId}?type=attachments.files` then answers every upload and delete of
the contact's files, with who and when, and nothing in the audit module changed. The history panel reads the contact's own history as before and,
for each kind that registers itself as a child of the shown kind (`history` in a module's web extensions: `{ parent, child, labelKey, hide }`; the
kernel's `ModuleExtensions` gained that one optional slot), its changes too, merged by time. While older changes of the record remain unread, the
files' changes stop where the loaded ones do, so the list never skips one. Each such entry carries a badge naming what it is ("Attachment") and
hides the fields that only link it to the record (`record_id`, `record_type`). The panel changes are small (`RecordHistory.tsx`, `model.ts`);
p07 owns that panel: if its round 4 rewrote it, the merge is the `useMemo` that builds `events` and the effect that reads `children`.

## Seeded demo files show who added them

The seeder finds the demo's administrator and accountant through the identity module's `IUserDirectory` (by `admin@` and `accountant@` of the
tenant's domain) and stamps `created_by`, `updated_by` and the times after saving (the data context stamps new rows with the seeding run itself:
no person). A workspace without them leaves the adder empty.

## Zip archives and mail messages

`zip`, `eml` and `msg` are accepted within the 20 MB limit, kept and offered as downloads only (never shown in the page, never opened or
unpacked). Server checks on the bytes, not the browser's word: a zip must begin with a local file header (or be an empty archive) and its
central directory must read; an `.eml` must be text without NUL bytes that opens with a header line and holds a header a message always has
(From, To, Date, Subject, Message-ID, Received ...); a `.msg` must be an OLE compound file (as the legacy Office kinds are). Content types are the
list's: `application/zip`, `message/rfc822`, `application/vnd.ms-outlook`. The old test that refused `a.zip` now refuses `a.rar`, `a.7z` and
`a.dll`, and new tests accept real zip and mail bytes and refuse a program renamed `.zip`.

## Not done: companies, branches and users taking attachments

Each kind adds five endpoints, their gate attacks and their rows in the reviewed maps, in other pieces' modules (tenancy, identity), and every
endpoint costs gate processor time (docs/decisions/p00-foundation-gate-cost.md) in a re-integration that may add 500 s at most. The mechanism is
ready: a module registers `AttachmentRecord(...)` on the server and adds its kind to `attachmentKinds` (and `recordRoutes`) in the web module.

## Gate backlog row 18 (closed by this round)

The plant (a client-supplied storage key read from an optional `version` query value, its tenant parsed from the key) passed every gate because
the probe cannot know another workspace's keys. Two source and structure gates now close it (`AttachmentGateTests`): the client inputs of each
attachment endpoint, read from the running application's handlers, must be exactly the reviewed ones (route id, the name header, `inline`,
`search`, `take`); and every call of the store outside the store takes the caller's tenant (`tenant.TenantId`) and a key from the file's row or
the store's own answer, whatever the client sent. Both have self-tests that plant the critic's variant and several others.
