# p07 — Audit trail

- Complete capture of who changed what (field-level old and new values) and when for every
  business record, including custom fields, in the same transaction as the change.
- Append-only: nobody, including tenant administrators, can edit or delete audit rows through
  the product, and the database refuses it for the application role.
- Per-record history panel on every form; a global audit log list (100,000+ rows in the demo)
  with filters by user, record type, record, field and date; export.

Compared against Odoo: find who changed a given field on a given record, and when.
