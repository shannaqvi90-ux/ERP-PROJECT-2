# p16 — Shared Contacts directory

Shared master data every later module (sales, purchase, HR, accounting) will use, and the
platform's main 100,000-record list for the Odoo comparisons. Added on the owner's decision
of 2026-10-02. It holds no business transactions.

- Contacts as organisations and people; people can belong to an organisation.
- Names in English and Arabic; email; phone numbers in international form (UAE +971 default);
  addresses with emirate, city, area, PO box and country; website; tags.
- Trade licence number and tax registration number stored as plain fields. No tax rule or
  number-format rule is implemented without a source from the Federal Tax Authority and owner
  sign-off (human gate).
- Default currency and credit limit as money (decimal, currency, rate, base amount).
- Archive instead of delete; duplicate warning on name, email, phone or tax number.
- Built on the shared list, form, search, custom field, attachment, audit, numbering, import
  and export mechanisms, so it is the reference consumer of each of them.
- Demo volume: 100,000 contacts in the demo tenant, plus a smaller second tenant used by the
  tenant-isolation gate.
- Covered by G1 and G2 like every other module.

Compared against Odoo: find one contact among 100,000; add a custom field to contacts and
filter by it; import 5,000 contacts; create a contact; archive and restore a contact.
