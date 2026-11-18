# Exchange rate on a date with no published rate

Checked: 2026-11-18 (no official text found yet that covers this case; see below). Signed off: the
owner, 2026-11-18 (needs-human #29). Owning piece: p08-currency.

## The rule

1. An amount in another currency is converted at the rate for the date of supply
   (docs/compliance/vat-rounding-and-currency.md, rule 3, signed 2026-10-10, #4).
2. When no rate is published for that date (a weekend or a public holiday), the latest rate dated
   before it is used, if it is at most the currency's `maxRateAgeDays` older: 7 days by default; a
   workspace may set 0 to 366 per currency, and 0 means only the date's own rate.
3. A rate dated after the date of supply is never used.
4. Past the limit there is no rate: the user is told the latest rate's date and must enter a rate.
5. The document shows the rate used and the date it is from.

## Source

The signed rule rests on Federal Decree-Law No. 8 of 2017 on Value Added Tax, Article 69, and its
Executive Regulation (Cabinet Decision No. 52 of 2017), Article 59(1)(k), as quoted with links in
vat-rounding-and-currency.md: "the exchange rate approved by the Central Bank of the UAE on the date
of supply". That text does not say what to do on a date with no published rate. Points 2 to 4 above
fill that gap by the owner's decision; they are not quoted from a publication.

## Owner's sign-off, 2026-11-18

Asked to choose (A: approve as it is; B: another limit; C: no fallback, the user enters the rate),
the owner answered: "A: aise hi manzoor" (A: approved as it is).

## Check again

Before the VAT and invoicing modules build on this rule, look for an official Federal Tax Authority
or Central Bank publication on dates with no published rate, and re-check Article 69 against the
current law text (later amendments exist). If an official text covers the case, it replaces points
2 to 4 and goes back to the owner.
