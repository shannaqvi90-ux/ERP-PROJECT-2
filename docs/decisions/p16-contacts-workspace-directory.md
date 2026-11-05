# p16 — Contacts are one directory for the whole workspace, archived and never deleted

Date: 2026-10-25. Piece: p16-contacts. Status: accepted (round 1).

## Context

Contacts (customers, suppliers, authorities and the people who work for them) are master data that
every later module refers to: a sales order names its customer, a purchase order its supplier, an
employee record its emergency contact. A workspace holds several companies (legal entities) that
often deal with the same customer. The owner's goal asks for one shared Contacts directory
(gauntlet/pieces/p16-contacts.md), archived instead of deleted.

## Decision

1. **A module of its own**, `contacts` (`src/Modules/Contacts`, schema `contacts`, one table
   `contacts.contacts`), with a public contract (`Erp.Modules.Contacts.Contracts`:
   `IContactDirectory.GetAsync`, `ContactInfo`, the permission keys and list key). Later modules read
   contacts only through it.
2. **Workspace-wide rows.** A contact carries `tenant_id` (row-level security enabled and forced,
   the standard `tenant_isolation` policy, the audit trigger: `ProtectTenantTable`) and no
   `company_id`: every company of the workspace sees the same directory, so a customer of the
   Dubai company and of the Abu Dhabi company is one record, not two that drift apart. It is not
   `IWorkspaceWide` either: that marker is for settings only someone holding the whole workspace may
   change; a contact is ordinary data any holder of the permission may keep current.
3. **Permissions.** `contacts.contacts.read`, `.create`, `.update` and `.archive`. Archiving (and
   restoring) is its own permission and its own endpoints (`POST …/{id}/archive`, `…/restore`); the
   edit (`PUT`) never changes the status. Like every workspace-wide record (users), a permission
   given in one company counts while the user works in that company (the session's permission
   scope); the company-role gate aims at company-owned rows and the contacts table has none.
4. **Never deleted.** There is no delete endpoint, and the migration revokes `DELETE` on
   `contacts.contacts` from the application role, so not even a bug in the application can delete a
   contact (a test checks the database refuses it: `Contacts_are_never_deleted_by_the_application`).
   An archived contact stays readable (documents keep naming it); an archived organisation takes no
   new people (`contactsOrganisationArchived`), while its people stay.
5. **People belong to organisations.** `organisation_id` references `(tenant_id, id)` of the same
   table, so the database itself refuses a link to another workspace's contact; the API also refuses
   an id it cannot see (`contactsOrganisationUnknown`, the same answer as an id that exists nowhere),
   a person (`contactsOrganisationNotOrganisation`) and an archived organisation. A check constraint
   keeps organisations out of other contacts and a contact out of itself. An organisation that people
   belong to stays an organisation (`contactsOrganisationHasPeople`). The organisation and the job
   title are a person's: for an organisation they are left empty rather than refused (the API's
   documented example body, which names both, is then a valid organisation for the gates).

## Alternatives considered

- **Company-owned contacts** (each company its own list, as `branches` are): the same customer
  would be entered once per company, and the duplicate warning would have to look across companies
  the user cannot see. Rejected. A later piece can add an optional "only for company X" restriction
  (Odoo's partner `company_id`) with `ProtectCompanyTable` on a nullable column if the owner asks.
- **Soft delete with a deleted flag**: an archived contact is not deleted, it is out of use; the
  word and the permission say so. No row is ever removed.

## Hooks left for pieces built beside this one

- **Numbering (p10)**: the contact's `code` (shown as "Reference", C000123) is typed or seeded; when
  p10 lands, a new contact takes the next number of a `contacts.contact` sequence where the code is
  left empty. No numbering of our own was written.
- **Custom fields (p09)**: the list and form are the kernel's; custom fields attach to the
  `contacts.contacts` list and the contact form the way p09 defines for every module.
- **Audit (p07)**: the kernel's audit trigger records every insert and change (who and when, field by
  field); the derived duplicate keys (`name_key` and the like) are left out of the change set.
- **Attachments (p11), import and export (p14)**: the list already prints and exports (PDF, CSV,
  XLSX through the reports module, `ListRows`); import of 5,000 contacts belongs to p14.
