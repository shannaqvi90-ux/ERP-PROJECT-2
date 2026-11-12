# p07 audit: the audit log at demo volume (round 4)

Reason for the round: GET /api/audit/log must answer every list request the gates send at demo volume (hundreds of
thousands of audit rows per tenant) with 200, on a busy machine as well as a quiet one. Under load critics saw 21 to 29
answers of G1ListVolumeTests not answered with 200 (deep skips, groupBy, record contains, `fields eq '<column>'`).

## What was measured (demo stack, tenant A, 740,581 audit rows, quiet machine, EXPLAIN (ANALYZE, BUFFERS) as the app role)

| Statement | Before | After |
|---|---|---|
| Page at `skip=370000` (default sort and `sort=occurredAt`): index scan of the whole tenant, every skipped row read in full | 297 ms execution, 63,800 buffers; request 0.57 s | ids skipped on a covering index, 101 rows read by join: request 0.165 s |
| `filter=fields eq 'name'` (29 rows): count + page | seq scan 172 ms + backward time-index scan 220 ms (246,852 rows filtered per worker); request 0.44 s | trigram index condition: request 0.007 s |
| `filter=fields eq 'email'` (200,015 rows), count | 0.68 s request | 0.59 s request |
| `search=z` (one letter, no trigram possible), count and page | about 1.0 s request | 0.43 s request |
| unfiltered count (every list request) | 133 ms, parallel seq scan, 3 processes | unchanged: the `record_type = ANY(...)` and the company policy are evaluated per row |

The slowest statement before: the deep page (297 ms, 3 processes). After: the count of an unfiltered or broad request (130 ms,
3 processes) and the one-letter search count; neither grows with the page.

## Findings and choices

1. **The GIN index on `fields` was never used.** `fields && '{x}'` (array overlap, p09's change a6bcedcd) is not
   leakproof (`arrayoverlap` has `proleakproof = f`), and the audit table has row-level security (tenant policy and the
   company policy). The planner may not use a non-leakproof operator as an index condition on a table with security
   quals, so every field filter read the whole tenant. `ILIKE` (`texticlike`) is leakproof, so the trigram indexes on the
   record's name were used. Choice: a stored generated column `audit.entries.field_list` (`|code|code|`, from the
   immutable function `audit.field_list(jsonb)`) with a trigram GIN index; the list binding's new optional `tokens` on a
   multi-valued column adds `field_list ILIKE '%|code|%'` before the exact array test, so the index narrows and the array
   test decides (exact, case-sensitive). The shared path stays one: `ListBinding.Holds`, used by every `string[]` column.
2. **Codes, not names, in `field_list`.** Names share their letters (every trigram of `name` is in `display_name`), so a
   trigram index of the names matched 211,000 rows to find 29 (measured, 330 ms). Each name is written as the first ten hex
   digits of its SHA-256 (in SQL by `audit.field_list`, in C# by `AuditFieldCodes.Of`; a test compares the two). The code is
   an index key only; it is not a security measure.
3. **A covering time index.** `ix_entries_tenant_id_occurred_at_uid` now includes `record_type`, `record_company_id` and
   `record_branch_id` (what the reader's rights read), replacing the plain one (same name, same keys; no extra index).
4. **Deep pages by id (kernel, additive).** From 1,000 skipped rows on (and not for a ranked search or a custom-field sort),
   `ListBinding.QueryAsync` skips on ids alone (`SELECT id ... OFFSET n LIMIT m`, an index-only scan once the table is
   vacuumed, which setup does after seeding) and joins the page's rows by id. The first form tried, `id IN (subquery)`, was
   planned as a per-row test in one hand-written run (4 minutes), so the join form is used. Every list gains it; the
   list contract (offset, total, order) is unchanged. A keyset cursor is still the cheap way to walk; a skip of hundreds of
   thousands rows still costs an index walk (about 80 ms for 370,000 rows).
5. **Not done, and why.** The unfiltered total is an exact count (the gates judge it); making it cheaper needs the company
   policy's per-row function call to be cheaper (a change of p02's policy) or a maintained counter (a hot row per tenant on
   every audit insert, rejected). Single-letter searches cannot use a trigram index.

## Round 3's tamper evidence is not carried

`audit.capture()` as SECURITY DEFINER with INSERT revoked from the application role (round 3, never integrated) conflicts with
the integration branch: p09's `customfields.audit_record_history()` inserts into `audit.entries` as the caller, and a clean
setup failed with `permission denied for function audit.capture` when a later module created its trigger. It needs its own
round (make p09's function a reviewed definer function too). The migration, its gate entries and tests were backed out.

## Record history names

* A choice shows the chosen option's label: `audit.choice.<schema.table>.<field>.<value>` in
  `web/src/modules/audit/i18n/{en,ar}.json` (the audit module holds the names of all recorded fields; the server's change
  texts and the web History panel both read them). Registered: `numbering.sequences.reset` (yearly, monthly, never).
  Other modules add their choice fields the same way. A value without a key shows as stored.
* Decimals: the server texts print the stored number as stored (no rounding anywhere in `AuditTexts.Value`). The web History
  shows a JSON number through `formatDecimal`; a built-in column has no declared decimals, so a stored `25.50` shows as `25.5`
  there (custom fields carry their declared decimals). A server-supplied scale per column would fix it; not done this round.

## Attachments in the record's History

`AuditRecordType` gains `ParentKindField` / `ParentIdField` (additive): rows of that kind name the record they belong to by
those columns. `GET /api/audit/records/{id}?type=<kind>` then also returns the rows of every kind whose parent columns name
that kind and id (JSON containment, new or old values: an upload is an insert, a removal a delete). p11 registers
`attachments.files` with `ParentKindField: "record_type", ParentIdField: "record_id"` (one argument added to its
`AuditedRecord` call). A rename of a file records only `name`, which does not name the parent, so a rename is not shown.

## Processor time

Adds one function, one column, one GIN index and a covering include to the audit table (about 57 MB of index per 750,000 rows)
and one migration; the work added to a gate run is the insert cost of the second GIN index in the volume fixtures (PostgreSQL
containers are not counted in verify.cpuSeconds) and four new tests; the requests of the gates cost less processor time.
