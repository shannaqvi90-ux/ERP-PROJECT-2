# p16 — The contacts list at 100,000, and finding one contact

Date: 2026-10-25. Piece: p16-contacts. Status: accepted (round 1).

## Decision

1. **The list framework as merged (p05)**: `contacts.contacts`, bound to the entity
   (`ContactsList.cs`), served by `GET /api/contacts/contacts` with the list query contract. Search
   fields: English name, e-mail, code, mobile, phone, tax registration number; a word in Arabic
   letters searches the English and Arabic names; a word of two to five Latin letters also matches a
   name's initials ("swar" finds Shamma Waleed Al Romaithi). All search fields sit in one GIN
   trigram index (`ix_contacts_search`); every sortable column (name, code, e-mail, city, created,
   changed) has its `(tenant_id, column, id)` index. Built-in views: Active, Organisations, People,
   Archived, By emirate, Newest first. The list prints and exports through the reports module.
2. **The form framework as merged (p06)**: `useRecordForm`, `RecordForm`, the kernel fields
   (`PhoneField`, `MoneyField`, `CountryField`, `LookupField`). One additive change to the kernel:
   `LookupField` takes an optional `filter` (the organisation picker offers active organisations
   only); without it the field behaves as before.
3. **Finding a contact from anywhere**: the command palette searches contacts (best match first,
   name, organisation and e-mail shown) and Enter opens the contact's form; G then O opens Contacts
   (C was taken by Companies); on the Contacts screen the search box has the focus and Enter opens the
   best match.
4. **Archived contacts stay in the list** with the status Archived (muted); the Active view hides
   them. The list framework has no default view per list yet; the lists of users and companies show
   inactive rows the same way.

## Measured

- `ContactsListVolumeTests` (trait Load=Timing, run alone after every other test): at 100,006
  contacts, searches by name, Arabic name, initials, e-mail, phone digits and TRN put the contact
  first within the 400 ms median / 1 s ceiling budget the users list has; sorted, filtered (kind,
  emirate, tags, credit limit), grouped, broad ("al", "محمد") and keyset page 31 stay within it; the
  duplicate check too.
- Comparison task `find-record`: our driver (`gauntlet/compare/drivers/ours/find-record.mjs`) offers
  the palette path (Ctrl+K, the name, Enter: 3 user actions and the name's letters), the palette with
  the shortest ranked prefix, the Contacts link or G O then the name and Enter, and the initials. The
  Odoo reference path is Contacts (apps menu or palette), the name, Enter, and a click on the single
  result. Results of this round's runs are in the notes for the integrator.

## Processor time this round adds to `./erp verify`

Measured on the builder's machine (busy with other verifies): the contacts web screen tests
(about 5 s), the contacts keyboard sweep (about 100 s: the open-record sweep and its control, about
45 s each), the contacts plant self-test (seven vitest runs, about 60 s), the palette sweep's extra
screen (about 10 s), the contacts module tests (about 20 s with their own gate database) and the
generic gates' attacks on eight more endpoints (G1 and G2 enumerate them; their share is reported in
the notes once the full verify ran). The timing test seeds 100,000 contacts (database time, not
counted in the processor-time maximum) and runs alone in the timing stage.
