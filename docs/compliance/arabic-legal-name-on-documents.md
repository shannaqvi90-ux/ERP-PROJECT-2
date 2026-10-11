# Arabic legal name on printed documents

Checked: 2026-10-10. Signed off: the owner, 2026-10-10 (needs-human #9). Owning piece: p02-tenancy
(company records), with p06-form-report for printed documents.

## Rule

- The Arabic legal name of a company or branch is optional. When it exists, Arabic documents
  print it; when it does not, they print the English legal name. This is the product's current
  behaviour (docs/decisions/p02-tenancy-company-records.md).
- Each company has a setting, off by default, that makes the Arabic legal name required for that
  company. While it is on, the company cannot be saved without an Arabic legal name.
- The English legal name is never saved empty (raised by the p06 r3 critic).

## Why optional

The VAT tax-invoice particulars do not require Arabic, and the Tax Procedures law lets the FTA
accept another language and ask for an Arabic translation when it needs one. Other laws (trade
licences, free zone rules) were not checked; a company that must show its Arabic name turns on
the setting.

## Sources

Cabinet Decision No. 52 of 2017 on the Executive Regulation of the VAT Decree-Law, and its
amendments, Article 59 (Tax invoices), as published by the Ministry of Finance:
<https://mof.gov.ae/wp-content/uploads/2024/10/Executive-Regulation-of-Federal-Decree-Law-No-8-of-2017.pdf>.
Clause 1 lists every particular a Tax Invoice must contain: the words "Tax Invoice"; the
supplier's name, address and TRN; the recipient's name, address and TRN where registered; the
invoice number; the dates; the description; unit price, quantity, rate and amount in AED; any
discount; the gross amount and the tax in AED with the exchange rate; the reverse-charge
statement where it applies. None of them is a language requirement.

Federal Decree-Law No. 28 of 2022 on Tax Procedures, and its amendments (up to Federal
Decree-Law No. 17 of 2025, effective 1 January 2026), as published by the FTA:
<https://tax.gov.ae/Datafolder/Files/Legislation/2025/Federal%20Decree-Law%20No.%2028%20of%202022%20-%20publishing%2003%2012%202025.pdf>

> Article 5 – Language. 1. Every Person shall submit the Tax Return and any data, information,
> records and documents related to Tax that he is required to submit or otherwise requested to
> submit to the Authority in Arabic. 2. Notwithstanding Clause 1 of this Article, the Authority
> may accept the Tax Return, data, information, records, and documents related to the Tax in any
> other language, provided that the Person provides the Authority with a translated copy of any
> of these in Arabic if requested by the Authority, as specified in the Executive Regulation.

UAE Electronic Invoicing Guidelines, Version 1.1, 1 June 2026, Ministry of Finance:
<https://mof.gov.ae/wp-content/uploads/2026/06/UAE-Electronic-Invoicing-Guidelines_V-1.1-01June2026.pdf>

> Note that the Electronic Invoicing framework is designed to support both the Arabic and English
> languages.
