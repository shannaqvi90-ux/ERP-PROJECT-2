# p09-custom-fields: the trail shows a custom field's meaning, not its stored value

Round 4 (critic r3's biggest gap: option keys and yyyy-mm-dd dates in history).

- The audit history, log, print and export use one path (`AuditTexts.Summary` on the server, `valueText` on the web).
  `AuditFieldLabel` now carries the select field's options (value, English and Arabic label), read through
  `ICustomFields.DescribeAsync` (the kernel contract; no table of another module is read). A select value is shown
  by the option's label in the reader's language; a value that is no longer an option shows as stored.
- Dates show dd/mm/yyyy, numbers with the declared decimals, yes/no as Yes/No (Arabic: the module's strings), lookups by the record's name
  (p07 r7's mechanism, unchanged).
- Gate: `CustomFieldRoundTwoTests.The_trail_shows_every_kind_of_custom_field_by_its_meaning_in_both_languages` exercises every
  kind in `CustomFieldTypes.All` (and fails when a kind is added without being listed) in the log line and the csv export, English and Arabic;
  web unit tests in `fieldLabels.test.ts`.
- Also: a required field with a default, sent empty by a form for an old record that holds no value, gets the default instead of
  "This field is required" (`CustomFieldService.SaveAsync`).
- Not done (routed): DateInput separators (p18), import-5000 health check and tenant export crash (p14); money custom fields and
  lookup targets other than contacts (open since round 1); lookup picker disambiguation; grouping by a custom date per month (19 s).
- Cost: no new endpoint, no processor time beyond a few test requests.
