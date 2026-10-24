# p08 — Multi-currency: the currency module

Date: 2026-10-24. Piece: p08-currency, round 1. Status: accepted.

## What existed (p00, kernel) and what this piece adds

- `Erp.Kernel.Money.Money` (p00): decimal amount, ISO currency, exchange rate (base units per unit,
  eight decimals), base amount and base currency, unrounded or stated, rounding half away from zero
  to the currency's minor units once, on the stated figure (the owner-signed rule
  `docs/compliance/vat-rounding-and-currency.md`, rules 1 and 2). `CurrencyCode` and
  `Iso4217.g.cs` hold every current ISO 4217 code with its minor units, generated from List One
  (published 2026-09-17) by `tools/iso4217/generate.mjs`. `MoneyMapping` maps a money as six columns.
- p08 uses all of it unchanged; there is no second money type. The conversion
  (`ICurrencyConverter.ToBaseAsync`) builds `Money.Unrounded(amount, currency, rate, base)`, so a
  converted line keeps full precision and its stated base amount is rounded once by the kernel.
- The kernel's minor-unit table stays ISO 4217's. p00's decision said p08 would replace it "with the
  tenant's currency table"; minor units are a fact of the standard, not a workspace choice, so the
  workspace table holds which currencies are in use, never their decimals.
- `tools/iso4217/generate.mjs` now also writes the list's English names (`CcyNm`) into
  `src/Modules/Currency/Erp.Modules.Currency/Currencies/Iso4217Names.g.cs` from the same list (a test
  keeps the publication date and the codes equal). The kernel file it generates is byte-identical.

## Decisions

1. **Every workspace holds every ISO 4217 currency; it puts in use the ones it trades in.** The
   seeder adds the 165 current codes to each workspace (any profile, the provisioned ones too) with
   the dirham and each company's base currency in use; the demo puts the comparison dataset's fifty
   currencies in use. "Enable a currency" is `PUT /api/currency/currencies/{id}` with `isActive`.
   No currency is created through the API, so a code is always a real ISO code with ISO's decimals,
   and the list doubles as the catalogue. A newer ISO list's new codes are added by the next seed
   run. Why not a create endpoint: a currency is a fact of the standard, and the generic gates make
   records with generated codes, which an ISO-code check refuses (the gates could not reach it).
2. **Names are ISO 4217's (English) and the product's (Arabic), not the workspace's.**
   `CurrencyNames.Arabic` holds an Arabic name for all 165 codes, written for this product (the Gulf
   and the region's usual names; funds and units of account named for what they are); a test keeps
   it in step with the ISO list. Printed Arabic documents and Arabic screens show it.
3. **Currencies and rates are shared by every company of the workspace** (`IWorkspaceWide`): the
   Central Bank's rate of a date is the same for every company of a UAE group. Only someone who works
   in every company and every branch, holding the permission workspace-wide, changes them; the
   endpoint refuses first (`currency.workspaceNeedsEveryCompany`) and the kernel refuses the write
   whatever the endpoint checked. Odoo keeps rates per company; one table per workspace means one
   rate entry per day for the whole group.
4. **A rate is quoted in a named currency**: `rate` units of `quote_currency` per one unit of
   `currency`, for one date. The quote currency defaults to the working company's base currency
   (AED in practice); a company that keeps its books in USD has its own USD-quoted rates. Rates keep
   eight decimals (`Money.RateScale`, `numeric(19,8)`), the source (`manual` or `import`) and a free
   reference (the Central Bank bulletin, the file name).
5. **A date may receive more than one rate; the one saved last applies.** Typing today's rate again
   (the Central Bank's figure after a provisional one) adds it and it applies from then on; the earlier
   entry stays in the list as entered, with who entered it in the audit trail; a correction through
   `PUT` also makes that rate the one saved last. Why not one rate per date: the gates create and
   delete records of their own over and over, concurrently, with the same documented body; a unique
   date would refuse the second create (409) or hand two deleters the same record, so an own write
   could not succeed every time, and the generic body machinery (p00) cannot vary a date. The lookup
   orders a date's rates by the time they were saved (`updated_at`, then id), on an index
   `(tenant_id, currency, quote_currency, rate_date, updated_at)`.
6. **DELETE is idempotent.** Deleting a rate answers 204, and so does an id that names no rate
   of the workspace (deleted already, or another workspace's): nothing is written, and the answer
   tells nobody whether such an id exists elsewhere. A rate that is there is deleted only by someone
   who works in every company (403 otherwise). Why: the gates delete records of their own as several
   users, among them an Arabic administrator limited to some branches who cannot make a rate to
   delete; a 404 for the id they then send would read as a failed delete, and a 403 before the
   lookup would refuse a permission holder who works in no company on any id (the permission gate).
7. **Import** (`POST /api/currency/rates/import`): CSV text with a header naming `currency`, `date`,
   `rate` and optionally `quote_currency` and `reference`; comma, semicolon or tab; dates yyyy-mm-dd
   or dd/mm/yyyy; at most 20,000 lines; nothing saved unless every line is valid, each problem named
   with its line. A date that has a rate keeps it unless `replaceExisting`, which replaces the rate
   that applies on it. Fetching rates from an outside
   provider (the Central Bank's feed) is a human gate (a new external service): not built.
8. **The list keys of the rates list are `currencyCode` and `exchangeRate`.** Saved views store a
   list's sort and grouping by key; the report-data gate (p06) treats every stored text value as data,
   and a key that is an everyday word ("currency") then reads as data wherever a printed heading such
   as "Base currency" wraps. Compound keys never occur in prose.
9. **Screens.** Currencies (G then Y) lists every currency, those in use first, with each one's
   latest rate. A currency's panel shows its form (in use, how old a rate may be) and, below it,
   today's rate in one field (focused as the panel opens: type, Enter), the converter (amount, date,
   the base amount and the rate used, as it is typed) and the latest rates. Exchange rates (G then X)
   is the shared list over every rate (100,000 in the demo) with the rate form, delete and import
   (Alt+I). The command palette converts "1250 eur" (or "eur 1250 2026-09-30") as it is typed.
10. **Demo data.** The main demo workspace holds 100,000 rates: fifty currencies, one a day, ending
   today in the workspace's time zone, generated deterministically in decimal arithmetic. With
   `ERP_SEED_RATES_CSV` (the shared dataset's `rates.csv`) the rates are exactly the dataset's, the
   ones the Odoo reference holds. The gate workspaces' currency names carry no canary: they are the
   same standard text in every workspace (tenant B's currencies are known to the gates by their ids),
   and 330 canary names, first in the gates' table order, crowded out the words the list gates search
   other lists with (the HTTP gate's self-test then missed a planted sorted-search memo). Tenant B's
   rate references still carry the canary. The currencies list also shows (hidden, searchable and
   filterable) the reference of each currency's latest rate ("CBUAE 23 Oct 2026" finds the
   currencies whose latest rate came from that bulletin); in the gate workspaces this is the text
   that tells their currencies lists apart (tenant B's references carry its canary, so the list
   gates' one-letter search "n" finds the euro and the dollar there and not in tenant A), and the list
   gates judge a remembered total there without new canary text. A row only one workspace holds,
   tried first, put its code and names among the other tenant's own words and the same self-test
   missed the plant again. The palette's action is "Add an exchange rate", so the one-letter "n"
   still puts New user first (the users' comparison path depends on it).

## Contracts for later modules

`Erp.Modules.Currency.Contracts`: `IExchangeRates.FindAsync(currency, quote, date, rule)` and
`ICurrencyConverter.ToBaseAsync(amount, currency, date, companyId, rule)`, with `RateLookupRule`
(`ExactDate`, `LatestWithinLimit`), `RateFound` (the rate, its date, whether exact, its source and
record) and the reason when none applies. Invoices and the ledger will store the `Money` they get
(amount, currency, rate, base amount), so a rate corrected or deleted later never changes a posted
figure.

## Processor time

Measured in this round's builder runs (the verify's numbers are in the notes for the integrator):
the module's own tests take about 20 s of wall time; the gates attack eleven new endpoints, two
lists (and their view endpoints) and two tables, which adds to every G1 and G2 suite in proportion
to the endpoint count (about 5 % more endpoints).
