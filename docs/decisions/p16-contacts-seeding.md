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

## Verify fixes after the first full verify (round 1, fixer 1)

What the first full verify failed on, and the cause of each:

- Identity's module-permission test hosted a test module named `contacts`; the real Contacts module
  now collides with it. The test module is `mailroom` (resource `parcels`); the test checks the same
  things.
- The `find-record` compare driver imported `find-user.mjs`; drivers stand alone (the harness rule
  stays), so the driver holds its own small copy of the prefix function.
- Contacts is the first entry of the navigation and its New contact ties with New user in the
  palette on "n". The two keyboard drivers (`new-user-keyboard`, `reach-screen-keyboard`) now press
  Down twice after Alt+M to reach Users (the entries are Contacts, Reports, then Settings) and the
  palette path types "new u". Their paths cost 2 and 4 more keys; the best paths (go-to keys) are
  unchanged.
- The isolation gate suffixes the documented example of a patterned field ("+971 4 123 4567" and
  "-<run>-<n>") when the result still matches the field's pattern. The contacts phone pattern was
  only the character shape, so the suffixed number matched it but failed the UAE length rule. The
  documented pattern now states the length rule too (as the tenancy phone pattern does), with a test
  holding it equal to the server's two checks.
- The gate fixture's contacts held four-digit values (the last group of a phone number, PO boxes
  from 1000). The gate compares each value tenant B holds with a random value of the same shape,
  and the gate's own writes fill the other tenant's role names with counters, so a random
  four-digit control matched a role of tenant A. In the gate fixture a phone number is one group of
  seven digits and a PO box has five digits. (Seven-digit PO boxes were tried and left the
  leaky-list self-test without a search word that tells the tenants apart; it passes alone with five
  digits, 19 min.) The demo data are unchanged.

Verify runs (all through the slots, beside other verifies, load up to 49 on 16 CPUs):

| Run | Result | Processor time |
|---|---|---|
| fixer 1, run 1 (986f055f + fixes to the driver and the test module) | dotnet: G1 (phone pattern, four-digit values); web: instrument mutation M96 not judged under load (passes alone); e2e: palette path | not taken |
| fixer 1, run 2 | dotnet: the leaky-list self-test (seven-digit PO boxes); e2e: find-user "invalid" (the harness's own page read, under load) | not taken |
| fixer 1, run 3 (4353ec48) | every stage green (dotnet 843, web 631, e2e 130, compare 487, build 6); only the processor-time ratchet failed | 12,017 s (dotnet 8,988, web 2,480, e2e 496, timing 54; maximum 10,500) |

The processor time of this piece: the builder's first full verify used 11,248 s, run 3 used 12,017 s,
on a busy machine; the integration branch with p08 passes at about 10,000 s. The gates grow with the
endpoints and lists of every module (G1 HTTP isolation 4,154 s of wall time, non-interference 2,093 s,
list volume 637 s). The maximum belongs to the owner and is not raised here.

## Fixer 2 verify (head 837e840f, product code equal to 4353ec48)

One more full verify through the slots (log verify-fixer-p16-contacts-r1-f2.log, folder .verify-failed/20261029T031922Z-233930). Every test green (dotnet 843, web 631, e2e 130, compare 487, build 6). Only check failing: verify.cpuSeconds, 11,506 s (dotnet 8,630, web 2,337, e2e 481, timing 59) against the maximum 10,500. Load 12.5 at the start, 3.1 at the end, highest 42. That makes four full runs between 11,248 and 12,017 s with the same result. No code changed in this session. The excess is in the shared gates (p00), which grow with the endpoint and list count; trimming them or raising the maximum belongs to the owner or the lead. An alone verify (ERP_VERIFY_ALONE=1) is the next judge.
