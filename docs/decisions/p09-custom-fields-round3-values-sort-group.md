# p09 round 3: option values, blanks last, grouping by any field, focus

Date: 2026-11-15. Status: accepted.

- **Option values keep the label's letters.** A value is the label's words joined; the first word
  keeps its first letter's case when it is an acronym in capitals (EXW, FOB), otherwise it starts
  lower case as before (Net 30 is net30). The value rule now accepts a leading capital
  (`^[A-Za-z][A-Za-z0-9]{0,39}$`), a superset, so every stored value (fOB included) stays valid and
  filters on it keep working. Filters spell a value exactly as the option does (unchanged).
- **The default of a select added in the same save.** The screen sent null for the default when the
  option had no value yet; it now sends the chosen option's English label and the server, which
  already resolves a default by value or label, stores the option's value. Covered by a unit and an
  end-to-end test.
- **Blanks last in both directions.** A descending sort by a custom field adds "has no value" as an
  earlier key; the keyset cursor treats blanks as last for custom columns in both directions.
- **Grouping by text, number and date fields** (a lookup stays ungroupable). A date groups by day
  by default and by month or year with a suffix, `groupBy=cfShipDate.month` or `.year`, through the
  new SQL function `customfields.value_date_trunc` (migration 20261115000000; runs as the caller, so
  row-level security applies). The kernel groups no other date column by month or year, so this is
  the only place the suffix exists. Number keys come back with the store's full scale (7.00000000).
  Gate cost: the gates enumerate registered columns, which carry no custom columns, so no extra
  gate requests; the contract gate now asserts every non-lookup custom column is groupable, and the
  function-count check rose from 12 to 13.
- **Alt+Shift+F after History opened.** Focus had moved to the panel, so no custom field held it;
  the shortcut now falls back to the custom field the cursor was last in (focusin tracking).
- **OpenAPI hook for p15.** The custom-values schema carries `x-erp-custom-fields` (definitions
  endpoint, a filter template, the key pattern). A per-workspace document is not produced: the
  document is cached once per process and the endpoint names the fields.
