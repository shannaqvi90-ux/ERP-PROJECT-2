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

## Round 5 fix 1 (G2CompanyRoleTests)

- Found: the audit log, history and change reads showed raw ids of other companies in the old values of a user's working-company row (tenancy.user_workplaces: company_id, branch_id, recent_company_ids) to a reader who holds the access permission only in the new company. Not caused by numbering; seen on the round-4 tree too (order-dependent: needs another test to have moved a user from company Y to X first).
- Fix (p07 module, minimal): AuditReader.ShownChangesAsync shows those ids as empty when the reader's permission for that kind of record does not cover the company; used by the log, a change and a history. Readers who hold the permission everywhere see the values as stored.
- AuditTrailTests.A_choice_shows_the_chosen_options_label_in_both_languages_never_its_code used the old word keys ("yearly"/"monthly"); the stored values are 1 and 2 since this round, so the test now uses them (same assertions: label in both languages; other kinds show the stored value).
- Targeted runs: Gates G2 + NumberingAuditGate + G1TenantSource + AuditGate, 103 of 104 passed (7 m 23 s); Erp.Modules.Audit.Tests 14 of 15, the one failure fixed above.
- Remaining: G2CompanyRoleTests still fails when run with the other G2 classes, only on GET /api/audit/records/{id} answering 200 with the id of a user_workplaces row that was in company Y when the gate took its snapshot and was moved to company X by a parallel test (the id is the row's own id in its own history). Alone the test passes (23 s). Narrowing the gate to skip moved rows was refused by the permission classifier (a gate change); left for the owner or p00.

## Round 5 fix 2 (integrator's failed alone verify of the merge, 2026-11-19)

Merged the integration branch (dbc81aac; one conflict in tests/Gates/tenant-bypass-sources.txt, both lines kept) and then:

1. **Erp.Kernel.Tests.BulkTimeoutTests (10 tests)**: fixed. Naming sequences while seeding builds the string catalog; the Kernel test assembly
   embedded a deliberately English-only key (`only.english`) and SlowSeedModule, added to the running app, brought that assembly's strings into
   the app's catalog. The incomplete strings are now an in-code assembly (`UntranslatedStringsAssembly`) used only by
   `LocalizationTests.A_key_without_a_translation_stops_the_catalog_from_loading` (same assertion). The embedded resources were removed.
   Run: Erp.Kernel.Tests BulkTimeoutTests + LocalizationTests, 32 of 32 passed.
2. **e2e audit.spec.ts "filters by a person chosen by name"**: a race in the spec, not in the product. The list shows its unfiltered rows
   until the filtered answer arrives, so "rows visible" passed before the request was dispatched. The spec now waits for the filtered answer
   (`page.waitForResponse`) before it counts requests. No timeout changed; every assertion kept. 3 runs of the whole audit spec and the
   numbering spec passed (stack b-p10-numbering, volume 2000).
3. **G1HttpIsolationShardTests / G1HttpIsolationTests ("Journal voucher · Al Noor Trading LLC")**: reproduced (share 0, 23 min). Diagnosed with a
   temporary query (not committed): at the time of the leak both tenants hold 23 sequences named exactly that. It is not a leak: the gate's
   own valid writes send the OpenAPI example company name "Al Noor Trading LLC" for both tenants, so each gets sequences named from it
   (company creation names them). The marker list of tenant B is read before tenant A's writes (WritePairsPhaseAsync takes `values` once; the
   public-example exclusion works on exact strings only), so a name composed from a public example is a B-only marker until A has written
   the same example. Product-side there is no honest change: the heading must carry the company's name (the critic's item 2). The fix is in
   the gate (p00): re-read the victim's markers against the attacker's data after the attacker's writes (the reverse direction already does at
   G1HttpIsolationTests.cs:712), or drop markers that contain a published example value.
4. **GateSelfTests.The_company_attack_catches_an_endpoint_that_widens_the_company_scope**: reproduced alone (8 m 57 s) and it PASSES on the
   integration branch tip (6 m 18 s), so it is caused by this piece. Cause: G1CompanyIsolationTests sends only `before.Strings.Take(25)` of
   company Y's unique text as parameter values (lines 363 and 521), in table order; numbering sorts before tenancy, and this round gives every
   sequence of company Y a unique name in two languages (15 sequences x 2 and more), so the 25 are used up before tenancy.companies.po_box,
   and the planted paging oracle (plant P1) never gets its value. Same gate-side class as 3: the sample should take values round-robin per
   table/column (or more of them), not the first 25 in table order. Not changed here (p00 machinery).
