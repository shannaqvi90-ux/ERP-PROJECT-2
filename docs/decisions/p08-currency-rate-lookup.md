# p08 — Which rate applies on a date, and what happens when a date has none

Date: 2026-10-24. Piece: p08-currency, round 1. Status: accepted as an engineering rule; the
statutory question below is with the owner (human gate).

## The rule (IExchangeRates.FindAsync, GET /api/currency/rates/lookup, GET /api/currency/convert)

1. The same currency on both sides is rate 1.
2. Only a rate quoted directly in the wanted currency is used. No inverse (1 / rate) and no cross
   rate (EUR to USD through AED) is worked out: each would introduce a rounding the person never
   entered, and a ledger must show the rate it used.
3. A rate dated on the date itself is used (`exact: true`); of several rates of one date, the one
   saved last (by the time it was saved, then its id).
4. Otherwise (`LatestWithinLimit`, the default) the latest rate dated before the date is used if it
   is at most the currency's `maxRateAgeDays` older (7 unless the workspace changes it, 0 to 366;
   0 means only the date's own rate). A week covers a weekend and the usual public holidays without
   reaching for a rate weeks old. The answer says which date the rate is from (`rateDate`,
   `exact: false`), and the screens say "the latest rate before then (date)".
5. A rate dated after the date is never used.
6. Otherwise there is no rate: 404 `currency.rateMissing` (none on or before the date) or
   `currency.rateTooOld` (naming the latest earlier rate's date and the limit), in the caller's
   language. `ExactDate` (`exact=true`) accepts only rule 3.

A currency out of use keeps answering from the rates it has (old documents still convert); new rates
are refused for it.

## Statutory question (human gate, not decided here)

`docs/compliance/vat-rounding-and-currency.md` rule 3 (owner-signed, from Federal Decree-Law No. 8
of 2017 Article 69 and Cabinet Decision No. 52 of 2017 Article 59(1)(k)): an amount in another
currency is converted to dirhams at the rate approved by the Central Bank of the UAE on the date of
supply, and the invoice shows the rate used. The record itself says Article 69 must be checked again
against the current text before p08 or the VAT module builds on it. Two things are therefore left to
the owner and are not implemented as statutory rules:

- whether, for a tax invoice, a date of supply on which the Central Bank published no rate (a
  weekend or holiday) takes the latest earlier published rate, and how old it may be;
- the re-check of Article 69 against the current Decree-Law text.

What this piece provides for the VAT module either way: the source and reference of every rate (so
an invoice can say "CBUAE, 23 Oct 2026"), the exact-date rule, and the date of the rate used.
Fetching the Central Bank's published rates automatically is a further human gate (a new outside
service); manual entry and file import are built.

## Rounding

The converter returns the kernel `Money` (unrounded line; its base amount rounded once to the base
currency's minor units, half away from zero, rule 1 and 2 of the compliance record) and, for
transparency, the exact product (`exactBaseAmount`): 1,250 EUR at 4.2875 is 5,359.375, stated
5,359.38; -1,250 mirrors to -5,359.38.
