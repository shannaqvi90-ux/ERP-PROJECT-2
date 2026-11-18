# p10 numbering: the audit of numbering changes reads on screen (round 5)

Round 4's critic found three faults: a hand-set next number was missing from the sequence's History, every
numbering row in the Audit log read Record "(empty)", and "Starts again" showed raw codes.

## Choices and reasons

1. **The hand-set next number is recorded on the sequence** (columns `next_set_from`, `next_set_to`,
   `next_set_period` on `numbering.sequences`, written by the same PUT that moves the counter). Alternative
   rejected: registering `numbering.counters` with `OwnerField = sequence_id` so counter rows show in the
   sequence's History. A counter is written by every document that takes a number, so the one change a user
   made would drown among thousands of automatic ones. The counter's own audit rows stay (who moved it, when).
   The History now says: Next number was 148, Next number set to 500, Set for the period 2026, with who and when.
   No new endpoint; no change to the audit or kernel modules.
2. **Every audited numbering row names its record.** The kernel's capture function takes the heading from
   `name_en` / `name_ar` / `code`. Sequences hold them (kind of document and company legal name, in each
   language, and the document type key), set by the application on seed, company creation and every save
   (`SequenceLabels`); a seed run names sequences made before this round. Counters take their sequence's names
   and given numbers take their own number from `BEFORE` triggers (`numbering.name_counter`,
   `numbering.name_issued_number`, invoker rights, pinned search_path, reviewed in tests/Gates/tenant-bypass-sources.txt).
   The three columns are left out of the recorded changes (trigger arguments `-name_en -name_ar -code`): they are
   headings, not data. A company renamed later keeps the old name in a sequence's heading until the sequence is
   next saved or the seed runs; the heading is "as it was called when it changed", like every other module's.
   Counters named before this migration get their name at their next write.
3. **Stored values and labels meet.** `reset` is a smallint (0 never, 1 every year, 2 every month), so the audit
   strings are keyed `audit.choice.numbering.sequences.reset.0/1/2` (they were keyed by words nothing stored).
   The kind of document is shown by name too (`...document_type.<key>` for every registered type, on sequences and
   given numbers). The field is called "Starts again" in the audit strings, as on the form. Gate:
   `NumberingAuditGateTests.Every_recorded_choice_value_of_the_numbering_tables_has_a_label_in_both_languages`
   reads each CHECK of the numbering schema (lists and whole-number ranges) and every registered document type
   and demands a label with Arabic letters in Arabic; a second test demands a naming column on every audited
   numbering table. A web test shows the codes read as labels in both languages.
4. The write-set gate for `numbering.sequences.update` now lists the three `next_set_*` columns it may write.

## Cost

No endpoint added. Three BEFORE-write triggers per counter / given-number insert (one indexed lookup). Gate
processor time added: one small gate class (two cheap database reads) on the existing gate environment.
Odoo keeps no history for sequences, so the comparison tasks are unaffected.
