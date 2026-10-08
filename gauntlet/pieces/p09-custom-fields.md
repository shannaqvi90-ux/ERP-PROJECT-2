# p09 — Tenant-defined custom fields

- A tenant administrator adds fields to registered record types: text, decimal number, date,
  boolean, single select, lookup. Label in English and Arabic, required flag, default.
- New fields appear automatically in forms, lists (column chooser), filters and search,
  import and export, the API and its documentation, and the audit trail.
- Filtering by a custom field across 100,000 records stays fast (indexed storage).
- Custom field definitions and values are tenant-isolated (G1) and permissioned (G2).

Compared against Odoo: add a custom field and filter by it (on Contacts once p16 exists).
