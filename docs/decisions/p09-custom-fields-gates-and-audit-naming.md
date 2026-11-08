# p09 — What the shared gates taught the first build

Date: 2026-11-06. Piece: p09-custom-fields, round 1. Status: accepted.

The first full verify (kept as the builder's evidence) failed 11 gate tests, all on the new surface.
Each was a real property of the first design, so each was fixed in the product, not in the gate; the
two gate-data additions are listed last.

- **A read and a save must have one shape.** The write-oracle gates send a record's own answer back as
  its save. A contact's `customFields` was a list of field descriptors in the answer and a map of
  values in the request, so the round trip was refused. The answer now has `customFields` (values by
  key, accepted back unchanged) and `customFieldDefinitions` (what a form shows). Test:
  `A_contact_read_and_sent_back_unchanged_changes_nothing`.
- **A request the schema example describes must pass.** The subject and write-set gates build a valid
  body from the documented examples; a field definition whose example kind and default disagreed, or
  that refused `decimals` on a text field, was never valid. Properties that do not belong to a field's
  kind are ignored; the examples agree (`text`, default `Dubai`).
- **An answer is the same every time.** The non-interference gate compares an answer in a shared
  process with a fresh one byte for byte; a row's custom properties came in database order. They come
  in the workspace's display order now.
- **The API's words are not the workspace's data.** A field's kind was stored as the word `date`; the
  print gate then took the heading "date" of a currency list for a tenant's value it must not print.
  The kind is a small number in the database (as numbering's reset is) and a word in the API.
- **Name the audited record.** The capture trigger names a record by `name_en`, `name_ar` and `code`.
  A definition's columns are named so (its labels and machine name); a value carries its field's machine
  name in `code`, so the audit log shows which field changed ("cfCustomerSegment") and the log's
  search finds it.
- **Seeded volume costs audit rows.** Every value is an audit row, and the gate workspaces are seeded at
  the volume of a demo. The gate workspaces hold two fields with values on six contacts; the demo holds
  four fields with values on every contact (the history copy is not written for seeding: its actor kind is
  `seed`). Printing or exporting the audit log pays for every row.
- **Review lists.** `tests/Gates/process-state-allowlist.txt` (the descriptor's list of kinds of records),
  `tenant-bypass-sources.txt` (the history trigger pins its search path), `client-module-state.txt` (the
  six field kinds the form offers), `endpoint-permissions/customfields.txt`, `permission-write-sets.txt`,
  `read-permission-writes.txt`.
- **Two gate changes.** `ListContractGateTests` accepts the workspace's custom fields after the
  registered columns of a list (hidden, filterable, not sortable or groupable, labelled in both
  languages, named `cf…`) and still requires the registered columns to match exactly; the write-set gate
  (`G2/WriteSets.cs`) lets a reviewed line name a column prefix (`custom_fields.*`), used by one line only:
  a contact's history entry for a custom field's change, whose column name a tenant chooses.
  `CustomFieldGateTests` is new: it fails a record kind registered for custom fields without its list
  (under the same permission), the model call, the audit registration or row-level security, and a
  custom fields function that is security definer, not stable or a trigger function without a pinned path.
