# p10 — Gates over the numbering module, and its comparison task

Date: 2026-10-25. Piece: p10-numbering, round 1. Status: accepted.

## Gates first

What the existing gates reach without change, because they enumerate the running app and the
database: the three new endpoints (G1 HTTP attack with tenant B's ids, codes and values in every
route, query and body field; the non-interference, write-oracle, process-state and list-answer
gates; G2 deny-everything and exactly-one-permission through every carrier), the three new tables
(row-level security enabled and forced, `tenant_id`, the company scope and company write policies,
the application role, the audit trigger, the shared-record rule through the company and branch
attacks: `sequences` is `ICompanyWide`), the list (list contract, saved views, printing), the strings
in both languages, the OpenAPI document and the licence gate. What this piece added or tightened so
they cover it:

- `tests/Erp.Gates.Tests/G1/G1NumberingContractTests.cs` (new, ratchet `g1.numberingContractAttacks`
  220): the take contract, which no HTTP route reaches. Bound to tenant A as a job and as an
  administrator, it takes and previews a number of every registered document type in every company of
  tenant B and, with every branch of tenant B, in tenant A's companies; a user of one company asks in
  the other, and a user whose permission is held in one company asks in the other. Each must be
  refused by the module's own check (the database's refusal counts only for system work), none may
  answer with a number, and tenant B's sequences, counters and issued numbers must not change
  (fingerprinted with their row versions).
- `tests/Erp.Gates.Tests/G2/NumberingWriteSetTests.cs` (new) and `tests/Gates/write-set-unexercised.txt`:
  every company holds its sequences from the start (no POST makes one), so the generic write-set gate
  cannot make a record for the edit; this gate sends `PUT /api/numbering/sequences/{id}` the generic
  gate's variants (valid, each true-or-false field turned, every other reset, the largest number of
  digits, a next number, a suffix) as a user holding exactly `numbering.sequences.update` and working
  everywhere, and judges every audited write: only that sequence's pattern columns and its own
  counters' `next_value`. A reader and a caller limited to one branch of the company are refused with
  nothing written.
- `tests/Gates/endpoint-permissions/numbering.txt`: the reviewed endpoint-to-permission map of
  `/api/numbering/` (three endpoints, each with why that permission and no broader one).
- `tests/Gates/permission-write-sets.txt`: `tenancy.companies.create` now also inserts the new
  company's `numbering.sequences` (the company-created handler, same transaction); the list's own
  saved views for `numbering.sequences.read`. `tests/Gates/read-permission-writes.txt`: those views.
- `tests/Gates/tenant-bypass-sources.txt`: the one `BeginAsync` in `DocumentNumbers.cs` (the short
  transaction of a sequence that may skip, bound to the caller's own session values).
- Web: `src/modules/numbering/keyboard.test.tsx` (the keyboard sweep for a reader, with a sequence
  open, on the list and with every row chosen; the control: the form's save keys write for a user who
  may change sequences), `screens.test.tsx`, `pattern.test.ts` (the server's shared examples), and the
  palette gate (`modules/shell/paletteActions.test.tsx`) knows the numbering permissions, list and
  record.
- Module tests (`tests/Erp.Modules.Numbering.Tests`): gapless numbers under 24 concurrent documents
  with a third abandoned (exactly 1…16, no gap, no repeat), a gapless take waiting for an open
  document and getting its number back, a standard take never waiting and leaving a gap, 20 standard
  takes at once, periods and branches, preview equal to the next take, a pattern that would repeat a
  number, tenant and company isolation of the contract, the audit trail and the document a number went
  to; the API's list, record, edit, next-number rules, every validation rule in both languages, the
  company and branch rules, versions, a new company's sequences.
- Gate backlog: no row is owned by p10.

Not done here (shared machinery owned by p00): the write-oracle gate cannot send an edit to a record
whose collection has no POST; the sequence edit carries no identifying or name field, so the oracle
does not select it today.

## Comparison task (gauntlet/compare/)

**configure-sequence** (existing task; ours driver built, end state and set-up writes declared for
ours): Document numbering (navigation) > the purchase order row (the working company's sequences come
first; the panel opens with the whole prefix chosen) > type `PO-2026-` > Tab > type `6` > Enter. Six
steps, eleven keystrokes, two clicks, no chord, no scroll. The reference's recorded best
(`gauntlet/reference/odoo/tasks/configure-sequence.json`): 17 steps, 32 keystrokes, 8 clicks, 2
scrolls, 4 chords, 3.9 machine seconds, 40.7 modelled human seconds (developer mode, Technical,
Sequences, search, open, prefix, size, save). Ours' measured run is in the notes for the integrator
(builder's run against a local stack).

No task tied; no tie is claimed.

## Processor time

The module's own tests take about 15 s of wall time (21 tests). The gates attack three new endpoints
and one list (about 3 % more endpoints than before); the two new gate classes each start one gate
environment and take a few seconds. The verify's numbers are in the round's notes.
