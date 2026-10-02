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
