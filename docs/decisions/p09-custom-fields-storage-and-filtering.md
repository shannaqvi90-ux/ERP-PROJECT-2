# p09 — Custom field values: typed rows, filtered through published SQL functions

Date: 2026-11-06. Piece: p09-custom-fields, round 1. Status: accepted.

## Context

A tenant adds fields (text, decimal number, date, yes/no, single select, lookup) to a kind of record
a module registers (contacts first). They must appear in the form, the list (column chooser, filters,
search), the API and the audit trail, and filtering 100,000 records by one must be fast (a plan or
statement test, not wall seconds). The owner's rules: tenant isolation by row-level security, money
never floating point, every write audited, a module never reads another's tables.

## Decision

- **Definitions** (`customfields.definitions`): one row per field: kind of record, a machine name
  (`cf` + the English label in camel case, made by the server, never changes), labels in English and
  Arabic (a blank Arabic label shows the English one), kind (never changes), required, default (as
  canonical text), decimals (numbers), options (single select, jsonb), lookup target, order, searched
  by the list's search box (text), in use. A field is switched off, never deleted: the records keep
  their values and the audit trail keeps its history. At most 40 fields in use per kind of record.
  Workspace-wide (`IWorkspaceWide`): every company shares them, so only someone who works in every
  company changes them (the kernel refuses otherwise, as for currencies).
- **Values** (`customfields.field_values`): one row per record and field, exactly one typed column set
  (`text_value`, `number_value numeric(28,8)`, `date_value`, `bool_value`, `ref_value`; a check
  constraint says so). Rows exist only for values that are set. Each kind has its own composite index
  `(tenant_id, field_id, value, record_id)` (text: on `lower(text_value)`; a trigram index serves
  "contains" and the search). Numbers are `numeric`, never floating point.
- **Why not a `jsonb` column on the record** (the audit trigger's `custom_fields` contract, p07):
  equality could use a GIN index but ranges (`gt`, `ge` on numbers and dates) and sorting could not
  without one expression index per field, which needs DDL from the application role at run time; and
  the consumer's table (contacts) would carry the values, so every module that gets fields would
  change its own migrations. A typed side table keeps one set of indexes for every kind of record
  and the consumer's tables untouched. The audit contract is honoured differently, below.
- **Filtering.** The kernel's `ListBinding` gets the workspace's fields as extra columns per request
  (`ListCustomFields`): filterable, hidden by default (the column chooser adds them), not sortable or
  groupable (a value held in another table cannot use a keyset index), text fields searched. A
  condition on a field becomes `row.Id IN (SELECT record_id FROM customfields.match_…(field, op, value))`.
  The `match_*` and `search_text` functions are `LANGUAGE sql STABLE`, run as the caller (never
  security definer: row-level security keeps them inside the caller's workspace) and have one branch
  per operator guarded by the operator argument, so the planner drops the other branches (measured:
  an `Index Scan` on the field's value index and a nested loop into the contacts primary key,
  6 to 8 ms over 100,000 contacts and about 270,000 values; `CustomFieldVolumeTests`).
- **No table mapped across modules.** The kernel publishes the functions as `CustomFieldSql`
  (table-valued `[DbFunction]`s returning ids). A consumer calls `modelBuilder.UseCustomFields()`
  (functions only, no entity with a table, no migration) and registers its kind of record with
  `ModuleBuilder.CustomFieldRecord`. `ModuleBoundaryGateTests` stays true: the consumer's DbContext
  maps only its own schema. `CustomFieldGateTests` fails a registration that lacks the model call,
  the list, the same read permission or the audit registration.
- **Values travel with the record.** There is no value endpoint: the contact's `GET`, `POST` and `PUT`
  carry `customFields` (the consumer calls `ICustomFields.EntriesAsync` and `SaveAsync` in the record's
  own transaction), so a value is read and written under the record's own permission, a refused value
  rolls the whole request back, and a required field is enforced on every path, API included. A list
  row carries the values as properties named by the field's key (JSON extension data), a lookup also
  its record's names.
- **Audit.** Both tables carry `audit.capture()` as every tenant table (kinds "Custom field" and
  "Custom field value" in the audit log, read with `customfields.fields.read`). A second trigger on the
  values, `customfields.audit_record_history`, writes the change into the history of the record that
  holds it as `custom_fields.<key>` with old and new value, which is the contract p07's capture
  function documents, so the contact's own history (read with `contacts.contacts.read`) shows who set a
  segment and from what. The record is named as its last audit entry named it.
- **Web.** The list's definition carries the tenant's own labels (`strings`, English and Arabic) for the
  fields and their options; `registerDynamicStrings` keeps them per identity (emptied when it ends)
  and `t()` finds them by key after the product's own strings. The form shows the fields in a
  "Custom fields" section (`kernel/forms/customFields.tsx`), values kept in the draft as text under
  `customFields.<key>`.

## Hooks left for later pieces (no copies)

- p14 (import and export): `ICustomFields.ValuesAsync(recordType, ids)` gives plain values by key for
  many records; `SaveAsync` validates and writes one record's values; `CustomFieldRecordType.AllIds`
  lists a kind's record ids; `ListBinding` gains the columns for any request that carries an
  `HttpContext` (the reports module's list printing reads `PageAsync`, which attaches the values).
  The static `Apply` and `Matching` do not know the fields yet.
- p15 (API documentation): the field keys are per tenant, so the OpenAPI document describes
  `customFields` as an open object and the row's extra properties as additional properties.

## Limits, stated

- Sorting and grouping by a custom field are not offered.
- A select's option labels shown in audit history are the stored option value.
- Switching a field off hides it everywhere; there is no delete (keys cannot be reused).
- Records that are deleted leave their value rows (contacts are archived, never deleted). A kind of
  record that deletes rows must delete its values (a `DELETE` hook is not built).
