# p10 numbering: tax document sequences (round 3)

Source of the rules: docs/compliance/vat-tax-document-numbering.md (owner decision #32, 2026-10-30).
Nothing here adds a rule; each choice below is how the product enforces one of them.

## Decisions

1. **A `Tax` flag on the document type, not a column on the sequence.** `NumberedDocumentType`
   gets `Tax` (default false); `sales.invoice` and `sales.creditNote` set it, and every tax type
   added later sets it in its own registration. The rule belongs to the type, so it holds for every
   company, including companies and rows made before this round, and a new company cannot start
   outside it. A type with `Tax` and without `Gapless` fails the module's start-up check.
2. **Rule 1, gapless always.** The update endpoint refuses `gapless: false` for a tax type with
   `validation.numberingTaxGapless` (English and Arabic); the screen shows the box ticked and
   disabled with a note. `DocumentNumbers` treats a tax type as gapless whatever the stored value
   says, so a row stored before this round (or edited in the database) still takes its numbers in the
   caller's transaction. The record and list show `gapless: true` and `tax: true`.
   Not done: rewriting stored `gapless = false` rows with a migration. The take path and the
   views already treat them as gapless, and a hand-written data migration would need a new
   snapshot and designer file for no change in behaviour.
3. **Rule 3, the next number.** Already refused below a number given and, on a gapless sequence,
   once a number of the period was given; since a tax type is always gapless, the same code path
   now gives the rule: settable only before the first number of the period, never backwards, sending
   the number it already stands at is accepted. The screen disables the field once a number of
   today's period is given and says why. Tests: `Owner_rule_3_...`.
4. **Rule 4, reset needs the period in the number.** Already checked by `NumberPattern.Check` for
   every type (yearly needs `{YYYY}` or `{YY}`, monthly needs the year and `{MM}`; the screen runs the
   same check). Test for a tax type added: `Owner_rule_4_...`.
5. **Rule 5, forward only and no repeat.** Prefix, suffix and padding already applied to the next
   number only. New: for a tax type, a change of prefix, suffix, padding, reset or per-branch is
   refused with `validation.numberingPatternRepeatsNumber` (naming the number it could give again)
   when the new pattern could print a number already issued. `SequenceEndpoints.RepeatedNumberAsync`
   looks at every period the new pattern counts in (today's, and each period that already has a
   counter, in the new reset's shape), every date and branch code it can print there, and asks the
   database for the issued numbers of that company and type that start with the printed prefix and end
   with the printed suffix; one whose digits read back (`NumberPattern.ValueIn`) as a value at or
   above that period's next value is a repeat. The issued-number unique index still stops a repeat
   at take time; this refuses it when the setting is changed, before any document hits it.
   Only tax types get the check in this round (other types keep the take-time refusal); widening it
   is one line if the owner wants it.
6. **Rule 6, one sequence per company and type.** Held by the unique index
   `(tenant_id, company_id, document_type)` and by there being no create endpoint; test
   `Owner_rule_6_...` checks both.
7. **Rules 2 and 7** hold already (the unique index of issued numbers; the audit trigger on
   `numbering.sequences`) and are untouched.

## Cost

New work is two queries on a tax type's update (counters, then one LIKE query per candidate date and
branch, usually 1 to 12) and nothing on the take path except one flag test. The tests add five API
tests, one concurrent test and two unit tests on one existing fixture (no new container), and two
web tests. Processor time of the round's full verify: see the end of this file.

## Odoo comparison

`configure-sequence` was a clear win in round 1 (6 steps, 13 keystrokes against 17 and 32); this
round does not touch that path (the prefix field still opens focused and selected, Enter saves).
No tie to explain.

## Other round-1 findings

- G1 false positive and the yearly/never/yearly stuck sequence: closed in round 2 (1be37979).
- Statutory human gate: closed by owner decision #32 (implemented above).
- Web stage timeouts under load (clientIsolation.test.tsx): p04's, not touched.
- The compare task covers prefix and digits only; the task files are the harness's, not changed.
- Gate backlog: no Open row names p10-numbering.

## Processor time of the round's full verify (builder run, 2026-11-01)

dotnet 7,543 s, e2e 654 s, web (with compare) 1,959 s: 10,156 s in all, measured while another
piece's integrator verify ran on the machine, so not an alone figure. Tests: dotnet 830, web 624,
e2e 128, compare 486 of 487 (see the failure below), build 6. The round adds a few seconds of
processor time (six API, take and unit tests on existing fixtures, two web tests).
