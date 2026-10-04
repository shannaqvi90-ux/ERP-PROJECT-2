# p06 — One record form for every module

Date: 2026-10-04. Piece: p06-form-report. Status: accepted.

## Decision

`web/src/kernel/forms/` is the only way a screen edits a record:

- `useRecordForm(spec)` holds the record and its draft: load, dirty tracking against what was
  loaded or last saved, save (create or update), server validation errors mapped to their fields
  (errors of fields not on the form go to the form's message), a conflict (409 `concurrency`)
  offered as "Show the latest version", discard, and read-only when the user may not change the
  record (the module passes its permission check as `canEdit`).
- `RecordForm` draws the header (title, "Unsaved changes", position "2 of 5", previous/next,
  Print, Discard, Save) and owns the keys. `FormSection` and `FormTabs` (tabs render lazily)
  group fields; a read-only form disables every field through one fieldset.
- Fields: text, multiline text, decimal (digits typed in Arabic-Indic are turned into Latin; the
  scale is enforced; the value stays a string, never a float), money (amount with currency),
  date, boolean, select, lookup (a typed search over another registered list). Every field is
  `div.field[data-field=name]` with its label, hint and errors linked by `aria-describedby`.
- Leaving with unsaved changes (navigation, opening another record, closing the panel, reloading)
  asks first. Escape on a changed form offers Save, Discard or Keep editing.
- Every record has its own address: the list screen's path and the record's id
  (`/tenancy/companies/<id>`), a new record `<screen>/new`; the list's search, filter and sort
  stay in the query. `useRecordPanel()` and the list read it the same way on every screen. Before
  p06 the users screen opened new records at `?new` and the others at `?open=<id>`; an older
  `?open=` link still opens its record and the address is rewritten to the path. A path, unlike a
  query, survives being bookmarked or shared through tools that drop queries, and the comparison
  harness reloads a record start screen from its path alone, so a record task can start on a
  record.

Keys (listed in the shortcut sheet under "Forms"): Ctrl+S and Ctrl+Enter save, Alt+Z discards,
Alt+PageDown / Alt+PageUp go to the next / previous record of the list the form was opened from,
Alt+R opens Print (PDF in English or Arabic), Escape closes. Keys match by key position too, so
Ctrl+S works on an Arabic keyboard layout ("س").

Converged: companies (with addresses), branches, company access, the workspace, users (new and
existing, with tabs), roles (edit and copy), my account.

## Why

Before p06 each screen had its own save button wiring, its own error mapping and its own keys:
tenancy registered Ctrl+S, identity Ctrl+Enter, and the users screen opened new records at a
different address. A user who learned one screen did not know the next. Every later module adds
dozens of forms; the same behaviour, keys and error handling must come free.

Compared with Odoo's edit-and-save: Odoo forms save on Alt+S (or auto-save on leaving) and discard
on Alt+J; ours saves on Ctrl+S (the key every desktop application uses), asks before losing changes
rather than saving them silently, and shows server validation on the field rather than a
notification.
