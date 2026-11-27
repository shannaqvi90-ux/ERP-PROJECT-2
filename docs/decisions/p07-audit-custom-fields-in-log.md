# p07 audit: custom fields in the global log (round 9)

Decision: a tenant's custom fields are first-class in the audit log.

- The log's Fields filter lists the tenant's active custom fields (every kind of record, once by key) by their own
  English and Arabic labels, as `custom_fields.<key>`. The definition endpoint, saved views and reports add them
  per request (`ListCustomFieldDefinitions.ExtendAsync`); a column opts in with `ListColumn.OpenChoicePrefix`.
  Filters accept any value with that prefix (no per-request database lookup; an unknown key matches nothing).
- A custom field's value row (`customfields.field_values`) is registered `FoldedIntoRecord`: the global log lists the
  owning record's change (which carries `custom_fields.<key>` with option labels) and leaves out the raw row, so one
  change shows once. The row stays in the trail; the record's History panel is unchanged. The kind is no longer a
  choice of the Record type filter.
- Seeded values have only the raw row (the history trigger skips seeds), so they no longer show in the log: they
  are starting data, not changes.
- People search with a character outside the BMP (emoji) answered 500: both pattern builders (`NameMatch.Pattern`,
  `ListSearch.Pattern`) escaped each UTF-16 half. A surrogate half is now kept as it is.
Cost: one extra `record_type <> $1` condition on the log; no new processor time of note.
Not done this round: export of the whole log past 200,000 rows, Arabic export names of people, History panel placement.
