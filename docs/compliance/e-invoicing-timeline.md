# UAE electronic invoicing: deadlines (information)

Checked: 2026-10-10. Nothing is implemented from this file. Electronic invoicing belongs to the
invoicing modules after the platform core; this records why they must plan for it.

## What the publication says

UAE Ministry of Finance, *UAE Electronic Invoicing Guidelines*, Version 1.1, 1 June 2026:
<https://mof.gov.ae/wp-content/uploads/2026/06/UAE-Electronic-Invoicing-Guidelines_V-1.1-01June2026.pdf>

| Entity type | Annual revenue | Last date to appoint an Accredited Service Provider (ASP) | Last date to implement the Electronic Invoicing System |
|---|---|---|---|
| Person | AED 50,000,000 or more | 31 July 2026 | 1 January 2027 |
| Person | under AED 50,000,000 | 31 March 2027 | 1 July 2027 |
| Government entity | n/a | 31 March 2027 | 1 October 2027 |

The format is Peppol PINT-AE (<https://docs.peppol.eu/poac/ae/v1.0.1/pint-ae/bis/>). The mandatory
fields are listed in *UAE Electronic Invoice Mandatory Fields*, Version 1.0, 23 February 2026:
<https://mof.gov.ae/wp-content/uploads/2026/02/UAE-Electronic-Invoice-mandatory-fields_V-1.0-23Feb2026.pdf>
(not read field by field yet).

## What it means here

The product's target customers (small and mid-sized UAE trading and manufacturing companies) fall
mostly in the under-AED-50-million group, with a deadline of 1 July 2027. The platform core
already keeps what electronic invoices need from it: the TRN (vat-trn-format.md), amounts with
currency, rate and dirham amount (vat-rounding-and-currency.md), and English and Arabic text.
