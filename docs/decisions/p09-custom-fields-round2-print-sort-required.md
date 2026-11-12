# p09 round 2 — print and export, sort and group, required, labels

Date: 2026-11-09. Piece: p09-custom-fields, round 2 (answer to the round 1 LOSS, 17ba3338). Status: accepted.

## Print and export take custom columns and filters

`ReportEndpoints.PrintListAsync` now extends the registered list with the workspace's fields
(`ListCustomFieldDefinitions.ExtendAsync`, the call the list definition endpoint already makes), so the
column check (`reportColumns`), the printed filter facts, the sort and group lines and the cell reader
all know the fields. Labels: the tenant's own words are not in the product's string files, so
`WebStrings.With(...)` returns a view that answers the fields' label and option keys first (English and
Arabic, an empty Arabic label shows the English one) and `ReportEngine.UseLabels` installs it for the
request (the engine is scoped). `WebStrings` stays an immutable singleton.
The list values already travel on each row (`ICustomFields.ValuesAsync` in the reader), so the cells
need nothing else.

**Failure message.** A download started from an anchor is cancelled by the browser without a word when
the server answers 4xx or 5xx. Fetching the file into a blob would have changed the download's address
(the comparison harness reads the print address of a download) and held a 100,000-row file in memory.
Instead the Print or export menu asks for the same selection as one screenful (`format=json`, the
request the server builds exactly as the file) when it opens; a refusal shows a role=alert message in
the screen's language (`lists.print.refused` for a 4xx, `lists.print.failed` otherwise) and the file
links are marked `aria-disabled` and do nothing. The download itself stays a native link. A failure
that only a long file build causes (a timeout) is not caught by this probe; the message covers the
selections the server refuses or cannot compose, which is what round 1 found.

## Why a filtered export took 40 to 55 seconds, and what it takes now

Measured on the 100,006-contact demo, a CSV of the 20,170 contacts of one segment: 39 s. The list answers a
200-row page in 0.2 s, but a print or export walks every page (100 requests). Two causes, both fixed:

1. A walk reads 2,000 rows a page (`ListRequest.WalkItem`, set only by the reports module's list print;
   `MaxWalkTake`, 2,000, replaces `MaxTake` for that request only; no client can ask for it). Each page of
   a custom filter costs one read of the matching ids, so ten pages cost a tenth of a hundred. The same
   walk of the unfiltered contact list went from 12.3 s to 4.9 s.
2. Npgsql prepares a statement a walk repeats, and after five runs PostgreSQL may use a generic plan
   made without the filter's value. It then thinks the matching set is small (136 rows instead of
   20,170) and joins it by a nested loop that rescans it for every contact: 177 million comparisons,
   5 to 12 s a page (`EXPLAIN` with `plan_cache_mode = force_generic_plan`; the log showed
   `execute _auto21` at 5.4 s). A statement must be planned for its own
   values. A list whose workspace defines custom fields asks for it for its own request
   (`ErpDbSession.UseCustomPlansAsync`, a transaction-local `set_config`, called by `ListBinding`). A
   role-wide `ALTER ROLE ... SET plan_cache_mode` was tried first and dropped: a migration may not alter
   roles, and set in the database set-up it changed the plans of every list (the audit log's deep, ranked
   pages failed the volume gate). After the change the same CSV takes 1.2 s, the PDF 1.4 s (55 s before).

## Decimals

A number field declares its decimals. `ListColumn` gains `Decimals` (null for every built-in column,
the number of decimals for a custom number column): the list shows "1,234.50" (web `formatValue`), the
printed document too (`ColumnSpec.Decimals`, minimum scale of `ReportFormatter.Decimal`). The form
already used the field's scale. The record history and the form's "Changed by" line rounded every
number to a whole number: that was p07's shared value formatter (`valueText` in
`web/src/modules/audit/model.ts`, which sent every number through the whole-number formatter, so the
native credit limit 25000.5 read 25,001). Fixed once there: a number with a fraction is shown through
the decimal formatter (25,000.5), and a custom number field's declared decimals come with the history
(`fields` below) so 1234.50 reads 1,234.50 in the history, the change panel, the audit log's panel and
the "Changed by" line.

## The trail names a field by its label

`GET /api/audit/records/{id}` and `GET /api/audit/log/{id}` return `fields`: for each
`custom_fields.<key>` the change mentions, the field's labels in both languages, kind and decimals
(`ICustomFields.DescribeAsync`, which also finds switched-off fields; a history keeps naming a field
that was later switched off). The audit log's one-line summary uses the label in the document's
language. A field the workspace never defined shows its stored name as before.

## Required on every save

`SaveAsync` now also looks at an existing record. A required field the record holds no value for (the
record was saved before the field was made required, or before the field existed) is given the field's
default on the next save of the record (a required field always has a default: definitions refuse
`required` without one); a required field without a default and without a value is refused
(`customFieldRequired`). Clearing a required value is refused on an update as on a creation. What
happens to existing records when a field becomes required: nothing at once (no mass rewrite, no audit
noise on records nobody touched, and the list still shows them); the default is written the next time
anyone saves the record, and the history shows that write as the person who saved it.

## A select's default

The default of a single-select field may be given as the option's value, or as its label in either
language, in any case; it is stored as the option's value. Round 1 refused `fob` for the option
labelled "FOB" because the option's generated value is `fOB`.

## A label may not be a built-in field's label

A new or changed label is compared (trimmed, white space collapsed, ignoring case) with the labels of
the record type's list columns, English with English and Arabic with Arabic (`customFieldLabelBuiltIn`
on `labelEn` or `labelAr`). The module has no copy of the screens' texts, so the kernel gets a small
`IWebTexts` (find a screen string in a language) that the reports module's `WebStrings` implements.

## Sorting and grouping by a custom field

Sortable: every kind but lookup; groupable: single select and yes/no. A migration adds
`customfields.value_number/value_date/value_bool/value_text(field, record)`: one-row scalar functions
(`LANGUAGE sql STABLE`, run as the caller, so row-level security applies; they read the unique index on
`(tenant_id, record_id, field_id)`). `ListBinding` binds each custom column as that function of the row's
id, so sort, keyset comparison and grouping use the list's existing machinery. The cursor of a page
carries the last row's value, read from the store (`ValueOfAsync`: one extra query per page, only when the sort
has a custom key). Cost: ordering all 100,000 contacts by a field is one index probe per row (the plan
cannot use a keyset index for a value held in another table, as round 1 recorded); filtering stays
index-first, and a page is requested only when someone sorts or groups. Gate cost: custom columns are per
tenant, so the gate matrices (which enumerate registered definitions) are unchanged; a new module test
walks the pages in both directions with nulls and checks the groups and the lookup refusal.

## Filter the list by a value from the form (the comparison path)

Alt+Shift+F in a custom field of an open record lists the records that hold the value typed there: the
field becomes a column, a condition `field is value` is set and the form closes (`FilterByFieldContext`,
provided by the list around the form it opens). This is a condition on that one field, as Odoo's
"Search Properties" is. Comparison "add a custom field and filter by it", ours:
Custom fields, New custom field, label, Enter, Contacts, prefix, Enter, click the field, value, Ctrl+S,
Alt+Shift+F = 11 steps against Odoo's 17; the strict column-chooser path (18 steps) remains offered.
The round 1 shortcut through the search box (a text field is searched with the contact's own fields)
is no longer the driver's path, so the win no longer rests on it.

## Arabic and the date box

"Lookup" is "سجل مرتبط"; the fields list's first column is headed "التسمية" (it shows the label in the
screen's language; "(بالإنجليزية)" stays on the form's English label box). A custom date field is a
text box that takes yyyy-mm-dd in either set of digits with a placeholder in the screen's language
(`forms.date.placeholder`); the browser's own date box wrote mm/dd/yyyy on Arabic screens because its
placeholder follows the browser's language.

## Money

A decimal custom field is a quantity or a score, not an amount: it has no currency, no rate and no base
amount, and it is never added to an amount. CLAUDE.md rule 2 (currency, rate used, base-currency amount)
applies to amounts, and the product has a place for them (the contacts credit limit, p08's rates). A
custom "money" kind would need p08's public contracts and a stored currency, rate and base value per
value, and its list and print columns would total per currency; that is a piece of its own and not done
here. The kind is called "Number" and is never formatted as an amount.

## API document

The contact row's custom values (`CustomFieldValuesAttribute` on the extension-data property) are
described in the OpenAPI document: one property per field named by its key, with what each kind holds
and where the keys come from (`GET /api/customfields/fields`). The keys are per workspace so they cannot
be listed in the document; p15 reads the workspace's fields from that endpoint.

## Processor time this round adds

New tests: 7 module tests (HTTP, one shared container, about 25 s of processor time), 8 web unit tests,
1 end-to-end test; no new endpoint; no new filterable or sortable column in a registered list (custom
columns are per request). The G1/G2 matrices are unchanged. Expected to add well under 100 s to the verify.

## Fixer verify (round 2, fix 1)

Full verify on fde7e319 (compose project b-p09-custom-fields), run through the verify slots: every test passed
(dotnet 1,023, web unit 727, e2e 150, compare 488, build 13). The G1 list volume gate (audit log deep pages) and the
G2 report data gate passed; the G2 failure of the earlier run ('prints 8800' for the audit.log XLSX) did not
repeat, and the same test run alone passed once too (8 m 35 s), so it was the race with parallel gate tests that
write role records, not the 2,000-row print walk. Processor time 18,978 s (dotnet 15,906, web 2,390, e2e 598,
timing 85) against the 18,700 s maximum: the only ratchet failure, 278 s over, within a re-integration's 500 s
allowance; the integrator's alone run decides. Load was low (1.3 at the start, 1.5 at the end).
