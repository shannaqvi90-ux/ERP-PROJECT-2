# p07-audit: every recorded value is shown the way its own screen shows it

Round 7 (critic r6's biggest gap: the Arabic history showed "sharjah → dubai" and "person").

Decision: keep one mechanism, string keys in the audit module's own strings, instead of reading
other modules' list definitions at run time (a module may use another only through contracts).

- Choices: `audit.choice.<schema.table>.<column>.<value>` in `web/src/modules/audit/i18n/{en,ar}.json`,
  read by the web (`valueText`) and by the server (`AuditTexts.Value`, for the log's print and export).
  Added for contacts (kind, emirate), companies and branches (emirate), tenants, users (language, digits),
  exchange rates (source), import sessions, exports, passkeys, custom field types and record types.
- Gate: `AuditGateTests.Every_recorded_choice_value_of_every_audited_table_has_a_label_in_both_languages`
  reads every CHECK (`col IN (...)`) of every audited table from the database and every choice column of
  every registered list that matches a recorded column of an audited table in the same schema, and fails
  until each value has a label in both languages (Arabic in Arabic letters). It also needs
  `audit.kind.<table>` for every registered kind of record. The older jobs-only gate stays.
- Country codes: the web uses `Intl.DisplayNames`; the server uses `Resources/countries.json`
  (English and Arabic names from CLDR), made by `node build/gen-audit-countries.mjs <out>`; .NET's
  `RegionInfo` was tried first and gave native names whatever the UI culture.
- Dates (`yyyy-mm-dd`) are shown dd/mm/yyyy (decision #47), read as a local day; numbers group thousands
  and keep their stored fraction (no binary floats).
- Record kind columns (`record_type`, `lookup_target`) show the kind's name (`audit.kind.<table>`).
- Look-ups by id already showed the referenced record's name; unchanged.
- Cost: no new endpoint; the gate adds one cheap database read. Not done: the Arabic export's "Changed by"
  column still carries the English name; the person picker's ranking; the comparison driver (p00 stub).
