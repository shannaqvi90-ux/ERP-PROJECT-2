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
- Comparison task `find-record` (`gauntlet/compare/drivers/ours/find-record.mjs`), run by the builder
  on 2026-10-25 against the shared Odoo rig with the shared dataset loaded into ours
  (`ERP_SEED_USERS_CSV` and `ERP_SEED_CONTACTS_CSV`), `--product both --repeat 3`: verdict **win**,
  ours judged on its whole path `palette-shortest` (Ctrl+K, "sha wal rom", Enter: 3 steps, 14 keys,
  0.40 to 0.44 s machine, 5.67 s human) against Odoo at its best per metric (5 steps, 30 keys, 1.14 to
  1.82 s, 17.91 s). The other paths: the palette with the whole name (3 steps, 32 keys: loses on keys,
  the capitals' Shift presses), the Contacts link or G O with the whole name (30 and 32 keys: a tie or
  a loss on keys), the shortest prefixes of any length ("sh w al r", 8 keys but a second of server
  time: two-letter words are not served by the trigram index), the initials ("swar": seven people
  share them). Odoo's search matches the typed text as one substring of the name, so it has no
  shorter path than the whole name; ours matches word prefixes in any order.

## Processor time this round adds to `./erp verify`

Measured on the builder's machine (busy with other verifies): the contacts web screen tests
(about 5 s), the contacts keyboard sweep (about 13 s for the record, the list and the control; the
record is swept at its own address, /contacts/contacts/<id>, since a sweep at ?open=<id> took every
key for a key leaving the screen and reopened the app 474 times, two minutes), the contacts plant
self-test (seven vitest runs, about 60 s), the palette sweep's extra screen (about 10 s), the contacts
module tests (about 20 s with their own gate database) and the generic gates' attacks on eight more
endpoints (G1 and G2 enumerate them: the HTTP isolation gate ran 142 endpoints in about 58 minutes on a
busy machine, against 134 before). The timing test seeds 100,000 contacts (database time, not counted
in the processor-time maximum) and runs alone in the timing stage, in about 10 s of test time.

## Round 2: fewer filter, group and sort columns, to cut the gates' processor time

The integrator's alone verify of round 1 measured 13,128 s against the 10,600 s maximum (owner decision
#31 allows a raise of at most 1,500 s). The shared G1/G2 gates send queries per filterable, groupable and
sortable column of every list, so the contacts list (19 columns) is the costliest list. The columns are
kept (and searchable where they were) but stop being separate filters or groupings where the search box
already finds the same value: `nameAr`, `mobile`, `phone`, `taxRegistrationNumber` (all found by search),
`country`, `defaultCurrency`, `creditLimitCurrency` (hidden columns, no filter or grouping) and `updatedAt`
(no sort; `createdAt` stays for "recent"). Filters stay on name, kind, organisation, e-mail, city,
emirate, tags, code, credit limit, status and creation/update dates; groupings on kind, city, emirate and
status. Measured effect: to be recorded from the round 2 verify.
