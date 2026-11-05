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

### Round 2, fix 1: gate-fixture PO box shape

The company-isolation gate replaces each value of company Y with a random value of the same shape and expects the
same answer as for Y's value. A bare five-digit PO box ("20002") became a random five-digit string that, by chance,
was a substring of a seven-digit phone or a 15-digit tax number of company X (substring search matches inside digits),
so the control answer listed a contact. The gate-fixture contact PO boxes are now a letter and five digits ("P20002"):
a random "q48213" cannot occur in X's data. Demo data is unchanged. GateSelfTests (leaky list) and the isolation gate
pass alone with this fixture (4 tests, 31 min under load). The company PO boxes of the tenancy fixture stay five digits
(not this piece's); their remaining chance is about 0.2 percent per value. Failures 2 and 3 of the builder's run
(GateSelfTests HTTP attack, G1ShellAnswers timing) are load flakes in p00's gate machinery, not edited here.

Verify of round 2 fix 1 (head dbec34ee, 2026-10-30, log kept in .verify-failed/20261030T151908Z-355466): the company-isolation
gate now passed in the full run. Failed: G1HttpIsolationTests (a request cancelled after 1 h 24 m; HttpClient 'client aborted'),
GateSelfTests HTTP attack (a 'DoesNotContain' on its list answers, 35 m), and 10 tests of web clientIsolation.test.tsx
(30 s timeouts). Processor time: dotnet 11,312 s, web 230 s, e2e 566 s. Load median 9.4 and peaks of 94 from other
workflows on the machine. These are the same kind of gate-timing failures as in round 2 and not contacts-specific.

Verify of round 2 fix 2 (head d7d8fd47, 2026-10-30, slot taken 16:47 UTC after 5,175 s of waiting; kept in
.verify-failed/20261030T184845Z-432784). Build, compare and e2e green; the company-isolation gate and the HTTP-attack
self-test passed. Failed: G1HttpIsolationTests (client aborted a request, 1 h 33 m, the same kind of failure as in
round 2 fix 1), web: the first test of clientIsolation.test.tsx timed out at 30 s and the other nine failed after it
(signInAs found no sign-in fields after the earlier test left the document), plus one 5 s timeout in
tenancy/screens.test.tsx. Processor time: dotnet 11,483 s, web 260 s, e2e 640 s. Load 20 to 36 from other workflows.
Measured: clientIsolation.test.tsx run alone on an idle machine takes 45 s and its first two tests 19.4 s and 19.3 s,
the same on the merge base without any p16 code (19.4 s and 19.3 s), so the 30 s limit leaves only 10 s of margin and
the test is not made slower by this piece. The G1 HTTP isolation test fails also in the verifies of p10 (critic r1),
p12 (four runs) and p16 round 1, and passes in others, with and without contacts: a timing flake of p00's gate under load.
No test, timeout or ratchet changed.

## Round 2, fix 3: verify result

Full verify of 802d25fc through the slots, started about 19:00 UTC on 2026-10-30 (slot taken at once), wall time 8,641 s, load 2 at the start, 11 at the end, highest 42 (other workflows running). Every test passed: dotnet 870, web 649, e2e 133, compare 487, build 6 (no failure, no skip). No code or test changed in this fix; the two earlier failures (G1HttpIsolation request abort, clientIsolation 30 s timeout) did not recur. G1HttpIsolation passed in 1 h 53 m, clientIsolation in 59 s for its 10 tests.
The only ratchet failure is verify.cpuSeconds: 15,934 s (dotnet 12,432, web 2,698, e2e 723, timing 82) against the maximum 10,600 s. The web figure is inflated by the busy machine (260 s in the previous run); dotnet is 12,432 s because the G1 HTTP isolation test alone ran 1 h 53 m under load. This is over the 1,500 s per-piece allowance (owner decision, needs-human #31), so the integrator's alone run decides what this piece costs.
Kept folder: .verify-failed/20261030T211650Z-468992.
