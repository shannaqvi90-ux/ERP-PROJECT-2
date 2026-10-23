# p08 — Gates over the currency module, and its comparison tasks

Date: 2026-10-24. Piece: p08-currency, round 1. Status: accepted.

## Gates first

What the existing gates reach without change, because they enumerate the running app and the
database: every new endpoint (G1 HTTP attack with tenant B's ids, codes and values in every route,
query and body field; the non-interference, write-oracle, process-state and list-answer gates; G2
deny-everything and exactly-one-permission matrices through every carrier), both new tables (RLS
enabled and forced, `tenant_id`, the application role, the audit trigger, the workspace-wide write
rule through the company and branch attacks), both lists (list contract, indexes, saved views,
printing), the strings in both languages, the OpenAPI document, the money gate and the licence gate.
What this piece added or tightened so they cover it:

- `tests/Gates/endpoint-permissions/currency.txt`: the reviewed endpoint-to-permission map of
  `/api/currency/` (eleven endpoints, each with why that permission and no broader one).
- `tests/Gates/permission-write-sets.txt`: the write set of every currency permission (rates insert,
  update of date, rate, reference and source only, delete, import), checked by the generic write-set
  gate, which now reaches every rate endpoint: the create's documented example is a date the gate
  workspaces never hold, entering the same rate again answers it as entered, and the update's example
  differs from the create's, so a record made for an edit is changed by it; the gate workspaces hold
  a fixed day (31 December 2025) so the import example also replaces a held rate.
- `tests/Gates/write-set-unexercised.txt` + `tests/Erp.Gates.Tests/G2/CurrencyWriteSetTests.cs`: the
  currency edit (`PUT /api/currency/currencies/{id}`) has no POST that makes a currency (every
  workspace holds the ISO list), so the generic gate cannot make a record for it; the new gate sends
  it the generic gate's variants (valid, in-use turned, the largest rate age) as a user holding
  exactly `currency.currencies.update` and working everywhere, and judges every audited write
  (only that currency's `is_active` and `max_rate_age_days`), plus a reader's and a one-company
  caller's refusal with nothing written.
- `tests/Gates/read-permission-writes.txt`: the two lists' own saved views.
- `tests/Gates/outside-process-methods.txt`, `tests/Gates/tenant-bypass-sources.txt`: the operator-only
  read of the comparison dataset's `rates.csv` at seeding (reviewed like the users' file).
- Web: `src/modules/currency/keyboard.test.tsx` (the keyboard sweep of both screens, with a record
  open, on the list and with every row chosen, for a reader; the control: the form's save keys write
  for a user who may change currencies) and the palette gate (`modules/shell/paletteActions.test.tsx`)
  now knows the currency permissions, lists and records, so every currency action is checked for its
  permissions and run from home, and the reader's sweep covers the currency screens.
- Module tests (`tests/Erp.Modules.Currency.Tests`): the rate lookup's rules, conversion and its one
  rounding (midpoint away from zero, the credit-note mirror, a line kept at full precision), rates
  entered, repeated, refused, corrected, deleted (and audited with the actor), imports whole or not at
  all, the in-use rule and a base currency kept in use, the workspace-wide rule for two kinds of
  limited caller, validation in Arabic, and each workspace converting with its own rates only.

Not done here (shared machinery owned by p00): the write-oracle gate (`G1WriteOracle`) cannot send an
edit to a record whose collection has no POST; the currency edit carries no identifying or name field,
so the oracle does not select it today. If a later endpoint of that shape has a name field, the
oracle needs p00's extension to send it to an existing record of the caller's (noted for the
integrator).

Gate backlog (`gauntlet/gate-backlog.md`): no row is owned by p08.

## Comparison tasks (gauntlet/compare/)

- **add-rate** (existing task; ours driver built): Currencies (navigation) > the euro's row (its panel
  opens with today's rate field focused) > type the rate > Enter. Measured on this machine under load:
  4 steps, 7 keystrokes, 9.0 s modelled human time; the reference's best: 10 steps, 8 keystrokes,
  22.1 s. The end state is declared for ours (`GET /api/currency/rates`, `items.exchangeRate`; the
  write `POST /api/currency/rates`) and set-up's writes are declared (`fixtures.ours`).
- **see-converted** (new task, both drivers): ours: Currencies > the euro > the converter's amount >
  type 1250 (4 steps, 4 keystrokes, 9.5 s). Odoo Community converts an amount only where it posts it:
  Invoicing > New > currency EUR > Add a line > price 1250 > Journal Items, which shows the line's
  credit in AED (11 steps, 13 keystrokes, 23.2 s); a palette variant (Ctrl+K "/invoi") too. Odoo
  dates a draft's rate lookup by its server's UTC day, so its set-up makes the task's rate for the
  user's day and the server's day.
- Odoo data: the shared rig already holds the dataset's 100,000 rates (`currency_rates`); no seed step
  was needed. Ours loads the same rates with `ERP_SEED_RATES_CSV`.
- Machine and wait seconds: the currency panel opened from its row is ready at once (the record
  replaces the row's facts when it arrives), so today's rate field has the focus as the click ends.

No task tied; no tie is claimed.
