# Rounding to the fils and conversion to dirhams

Checked: 2026-10-10. Signed off: the owner, 2026-10-10 (needs-human #4). Owning pieces: p00-foundation
(kernel `Money`), p08-currency (rates), and later the VAT and invoicing modules.

## Rule

1. Amounts in dirhams (AED), including tax amounts and base-currency amounts, are rounded to the
   fils (2 decimals) by mathematical rounding: half away from zero. 0.005 becomes 0.01, and
   -0.005 becomes -0.01, so a credit note mirrors its invoice. This is what the kernel `Money`
   type already does (docs/decisions/p00-foundation-money.md). The regulation does not define
   "mathematical rounding" further; half away from zero is the meaning the owner signed off.
2. Rounding happens once, on the figure that is stated. Lines and tax-category subtotals keep their
   full precision; the invoice total is rounded to 2 decimals. Electronic invoices state any
   difference in the optional "Rounding Amount" field.
3. An amount in another currency is converted to dirhams at the exchange rate approved by the
   Central Bank of the UAE on the date of supply. The invoice shows the rate used.

## Sources

Cabinet Decision No. 52 of 2017 on the Executive Regulation of the VAT Decree-Law, and its
amendments, as published by the Ministry of Finance (unofficial translation):
<https://mof.gov.ae/wp-content/uploads/2024/10/Executive-Regulation-of-Federal-Decree-Law-No-8-of-2017.pdf>

> Article 61 – Fractions of Fils. Where the Tax chargeable on a supply is calculated to a fraction
> of a Fils, the Taxable Person is permitted to round the amount to the nearest Fils on a
> mathematical rounding.

> Article 59, Clause 1(k): The Tax amount charged under the provisions of the Decree-Law expressed
> in AED, together with the rate of exchange applied where the currency is converted from a
> currency other than the UAE dirham.

Cabinet Decision No. 149 of 2026 (1 September 2026, effective 1 October 2026) amends other
articles of the same regulation and leaves Articles 59(1) and 61 unchanged:
<https://mof.gov.ae/wp-content/uploads/2026/09/Cabinet-Decision-No.-149-of-2026-Amending-Certain-Provisions-of-The-Executive-Regulation-of-VAT-EN.pdf>

UAE Ministry of Finance, *UAE Electronic Invoicing Guidelines*, Version 1.1, 1 June 2026:
<https://mof.gov.ae/wp-content/uploads/2026/06/UAE-Electronic-Invoicing-Guidelines_V-1.1-01June2026.pdf>

> Rounding off: Rounding off is applicable at the invoice level total up to 2 decimal places.
> Rounding off is not applicable at the tax category level or line-item level.

> Rounding off: The field "Rounding Amount" is an optional field and the value is to be provided
> by the issuer of the Electronic Invoice, if applicable.

Federal Decree-Law No. 8 of 2017 on VAT and its amendments, as published by the FTA (amendments
up to Federal Decree-Law No. 18 of 2022):
<https://tax.gov.ae/DataFolder/Files/Legislation/Federal%20Decree-Law%20No.%208%20of%202017%20and%20amendments%20-%20For%20Publishing.pdf>

> Article 69 - Currency Used on Tax Invoices. If the supply is in a currency other than the UAE
> Dirham, then for the purposes of the Tax Invoice, the amount stated in the Tax Invoice shall be
> converted into the UAE Dirham according to the exchange rate approved by the Central Bank of
> the State at the date of supply.

The Ministry of Finance has announced later amendments to the VAT Decree-Law that support
electronic invoicing. The copy above lists amendments only up to 2022, so Article 69 must be
checked again against the current text before p08 or the VAT module builds on it.
