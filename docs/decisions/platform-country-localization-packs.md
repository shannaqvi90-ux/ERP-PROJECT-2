# One product, a localization pack per country

Date: 2026-11-22. Owner's decision (gauntlet/needs-human.md, the row of 2026-11-22 on more countries).

## Decision

Bizora ERP stays one product. Its core (ledger, documents, stock, users, permissions, audit, design,
API, modules and licences) holds no country's statutory rule. Everything that differs by country
lives in a **localization pack** for that country, sold and switched per tenant through p17's modules
and licence files.

A pack holds, for its country:

- taxes as data: tax codes, rates, jurisdictions (for example provinces), when each applies, and
  rounding; one document line may carry more than one tax (for example GST with a provincial tax);
- chart-of-accounts templates and default posting accounts;
- statutory returns and reports (for example the UAE VAT return, Saudi Arabia's ZATCA return,
  Canada's GST/HST return) and the e-invoicing format and channel (for example Peppol PINT-AE in the
  UAE, ZATCA Fatoora in Saudi Arabia);
- payroll rules (for example WPS in the UAE, GOSI in Saudi Arabia);
- languages and formats: the languages it adds (for example French for Canada), date, number and
  calendar formats (for example the Hijri calendar beside the Gregorian one);
- where the tenant's data must be hosted, when the country's law asks for it.

Every rule in a pack comes from that country's official publications, with its link and the date
checked, and is signed off by the owner or a local accountant before it is used (CLAUDE.md rule 8).

## Order of countries

1. The United Arab Emirates (now).
2. Saudi Arabia: the closest (Arabic, a VAT of the same shape); new are ZATCA e-invoicing and its
   rate.
3. The rest of the GCC.
4. Farther markets such as Canada, usually with a local partner or accountant.

## What this asks of the modules built now

- Each tenant (and company) carries its country and locale; screens and documents follow them.
- The accounting module's tax engine reads taxes, rates and rules from the country pack; no rate or
  rule is written in code.
- Charts of accounts, statutory reports and document layouts are templates a pack supplies.
- The string catalogue takes more languages than English and Arabic without code changes.
- A new country is a planned piece (its pack), never a fork or a separate product.
