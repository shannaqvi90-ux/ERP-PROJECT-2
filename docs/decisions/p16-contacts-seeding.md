# p16 — Seeding 100,000 contacts, and the comparison dataset

Date: 2026-10-25. Piece: p16-contacts. Status: accepted (round 1).

## Decision

1. **Named contacts** in every workspace with demo or gate data (Falcon Building Materials LLC and
   its purchasing manager Hamad Saeed Al Ketbi, Gulf Pipe Supplies FZE and Priya Nair, an archived
   Oasis Retail Trading LLC, Muscat Marine Services LLC in Oman): screens, tests and the end-to-end
   suite name them. Fictional names; phone numbers in the 555 range; e-mail addresses under example
   domains of the contacts' own businesses, never the workspace's (a customer is not staff).
2. **Generated contacts up to the tenant's volume** (`./erp up`: 100,000 in Al Noor, 1,000 in Gulf
   Steel; the verify stack 2,000; the gate fixture 25 per tenant): about three in ten organisations,
   most people belonging to an organisation made shortly before them, UAE addresses by emirate and
   area, tags, licence and TRN for most organisations, credit limits in AED. Deterministic (seeded
   by the tenant's volume seed). Loaded with the kernel's `BulkInsert`, so row-level security checks
   every row and the audit trigger records it. `Erp:Seed:ContactsVolume` gives the main tenant
   another count (the timing budgets seed 100,000 contacts beside 200 users).
3. **The comparison dataset**: `ERP_SEED_CONTACTS_CSV=gauntlet/compare/data/out/contacts.csv`
   (made by `node gauntlet/compare/data/generate.mjs`, not committed) loads exactly the dataset's
   100,000 contacts into the demo tenant, the same ones the Odoo reference rig holds (kind, names in
   both scripts, organisation by reference, e-mail, phone, mobile, address with emirate code, PO
   box, country, website, licence, TRN, tags, credit limit). It sits in the same folder as
   `users.csv` (one read-only mount, `compose.yaml` `Erp__Seed__ContactsCsv`), and the verify stack
   never uses it.
4. **The gate fixture's contacts must not answer for a company's values.** The company and branch
   isolation gates send company Y's texts (its legal name, phone numbers, TRN) as search words and
   require the same answer as for a value that exists nowhere. Workspace-wide contacts that happened
   to hold every word of such a value would answer (the first gate run of this round: codes C000022
   and a contact under alpha.example answered "+971 6 000 0022" and "Alpha Steel Industries").
   Generated contacts therefore carry no zeros after the area or mobile prefix, codes of letters in
   the gate fixture (C-AAB), and no workspace domain in their addresses. Tenant B's addresses sit under
   its canary (a contact address shown to tenant A is a leak the gates see).
