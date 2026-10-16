# p00 — Money: decimal value type, numeric columns, decimals as JSON strings

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Decision

- `Erp.Kernel.Money.Money` is a value type holding `Amount`, `Currency` (ISO 4217),
  `ExchangeRate` (base units per unit of currency) and `BaseAmount` — the four facts CLAUDE.md
  rule 2 requires. `Money.Create` refuses amounts with more decimals than the currency allows,
  non-positive rates and a base-currency amount at a rate other than 1, and rounds the base amount
  half away from zero to the base currency's minor units.
- Columns: `numeric(19,4)` amounts, `numeric(19,8)` rate, `char(3)` currency, mapped by
  `MoneyMapping.MoneyProperty(...)` as an EF complex property.
- JSON: every `decimal` is serialised as a string (`"1234.50"`) and the OpenAPI document declares
  it as `type: string, format: decimal`, so no JavaScript client parses money into a float.
- Minor units per currency are a fixed table for now (AED/USD 2, KWD/BHD/OMR 3, JPY 0); p08
  (multi-currency) replaces it with the tenant's currency table.
- Gates: no `real`/`double precision`/`money` column in any schema; no `float`/`double` in server
  code; no `parseFloat` in web code.

## Why

- Floating point cannot represent 0.10 exactly; ledgers must balance to the fil.
- `numeric(19,4)` holds amounts up to 10^15 with four decimals (enough for 3-decimal currencies and
  unit prices); the rate needs more precision.

## Rounding rule: signed off (2026-10-11)

The rounding rule is no longer an engineering default. It is the owner-signed statutory rule
`docs/compliance/vat-rounding-and-currency.md` (checked and signed off 2026-10-10, needs-human #4),
taken from Cabinet Decision No. 52 of 2017, Article 61, and the Ministry of Finance's UAE
Electronic Invoicing Guidelines v1.1:

- Rule 1: dirham amounts, tax amounts and base-currency amounts round to the fils by mathematical
  rounding, half away from zero (0.005 to 0.01, -0.005 to -0.01). `Money.RoundToMinorUnits` is that
  rule in one place, and `Money.Create` rounds the base amount with it. Behaviour is unchanged from
  2026-10-02; `MoneyTests.Dirham_amounts_round_to_the_fils_half_away_from_zero` and
  `Base_amounts_at_a_fils_midpoint_round_away_from_zero_and_mirror` pin it, including the
  midpoints where banker's rounding would differ (0.025 to 0.03).
- Rule 2 (round once, on the stated figure; lines and tax-category subtotals keep full precision)
  governs *when* to round. `Money` is the type of a stated figure: it refuses an amount with more
  decimals than its currency, and `Add` sums base amounts already rounded. Line and subtotal
  arithmetic that must keep full precision is therefore done on `decimal`, and the stated total is
  turned into `Money` once. p08 (multi-currency) and the VAT/invoicing modules own that arithmetic
  and must follow rule 2 there; nothing uses `Money` for line totals yet (critic p00 round 9).
- Rule 3 (Central Bank rate on the date of supply) belongs to p08's rate table.

The minor-unit table in `CurrencyCode.MinorUnits` is an engineering table for currencies other
than the dirham (the compliance record covers AED, 2 decimals, the fils). p08 replaces it with the
tenant's currency table, which must take minor units from ISO 4217 as published.
