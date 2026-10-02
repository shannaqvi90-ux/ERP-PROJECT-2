# p08 — Multi-currency

- ISO 4217 currency list with minor units; tenant enables the currencies it uses; base
  currency per company (AED by default).
- Exchange rates by date, with source recorded (manual entry or import); rate lookup by date
  with clear rules for missing rates. Fetching rates from an external provider is a human
  gate (new service).
- Every amount stored with currency, rate used and base-currency amount, decimals only,
  rounding by currency minor unit.
- Conversion service in contracts for later modules; screens for currencies and rates
  (100,000 demo rate rows).

Compared against Odoo: add an exchange rate; see an amount converted to base currency.
