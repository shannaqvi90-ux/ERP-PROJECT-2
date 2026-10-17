# p00 — Money: decimal value type, numeric columns, decimals as JSON strings

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Decision

- `Erp.Kernel.Money.Money` is a value type holding `Amount`, `Currency` (ISO 4217),
  `ExchangeRate` (base units per unit of currency) and `BaseAmount` — the four facts CLAUDE.md
  rule 2 requires. `Money.Create` refuses amounts with more decimals than the currency allows,
  non-positive rates and a base-currency amount at a rate other than 1, and rounds the base amount
  half away from zero to the base currency's minor units. (Round 11: rounding follows rule 2 of
  the compliance record; see below.)
- Columns: `numeric(19,4)` amounts (since round 11: `numeric(28,10)` amount, `numeric(19,4)` base
  amount, plus a `boolean` stated flag), `numeric(19,8)` rate, `char(3)` currency, mapped by
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
  is now in the type itself (round 11, closing the critic's gap of rounds 9 and 10). Until round
  10 `Money.Create` refused any amount with more decimals than its currency and `Add` summed base
  amounts already rounded, so three lines of USD 1.00 at 3.6725 totalled AED 11.01 instead of
  11.02. Now:
  - A `Money` is *unrounded* (a line or a subtotal, the default) or *stated* (`IsStated`, made by
    `State()`). An unrounded money keeps its exact amount; its exact base amount is always
    `Amount × ExchangeRate` (`ExactBaseAmount()`), because `Add` only adds figures at the same
    rate. `BaseAmount` is that exact figure rounded once (what the figure states on its own, for
    display), and is never what a total adds.
  - `Add` of two unrounded figures sums the exact amounts and rounds the base of the sum once:
    the three lines above give 11.0175, stated 11.02. `Add` of two stated figures sums what each
    stated (a statement of two invoices that each stated AED 3.67 shows 7.34). Adding a stated
    figure to an unrounded one is refused, since part of the total would be rounded twice.
  - `State()` is the single rounding: the amount to its currency's minor units and the base
    amount from the *exact* base (not from the rounded amount × rate), both half away from zero.
    `RoundingAmount()` and `BaseRoundingAmount()` give the difference stating adds, the value an
    e-invoice may put in its optional "Rounding Amount" field.
  - `Money.Unrounded(...)` takes a line amount with up to 10 decimals (quantity × unit price) and
    never rounds it; more decimals, or a figure larger than the columns hold, is refused rather
    than rounded silently. `Money.Create(...)` is unchanged for an amount as entered: it must fit
    the currency's minor units.
  - Columns: `{prefix}_amount` is now `numeric(28,10)` so a line is not rounded on its way to the
    database; `{prefix}_base_amount` stays `numeric(19,4)` (always rounded to at most 3 decimals);
    a sixth column `{prefix}_stated boolean` keeps whether the figure was stated, so a stated total
    read back still adds as stated. No table used `MoneyProperty` yet, so no migration changes.
  - JSON keeps the same five fields (`IsStated` and the exact base are not in answers; clients do
    no money arithmetic). Money is not read from request bodies.
  - Tests in `MoneyTests` pin the critic's case, full-precision lines, subtotals in any grouping,
    the stated base coming from the exact base, the one rounding at a midpoint and its credit-note
    mirror, stated figures adding as stated, the refusal to mix, the bounds, the column types and
    the JSON shape. Every earlier Money test is unchanged and passes.
  - Behaviour otherwise unchanged: a single `Money.Create` still reports the same rounded
    `BaseAmount` (100.05 USD at 3.6725 is AED 367.43).
- Rule 3 (Central Bank rate on the date of supply) belongs to p08's rate table.

The minor-unit table in `CurrencyCode.MinorUnits` is an engineering table for currencies other
than the dirham (the compliance record covers AED, 2 decimals, the fils). p08 replaces it with the
tenant's currency table, which must take minor units from ISO 4217 as published.
