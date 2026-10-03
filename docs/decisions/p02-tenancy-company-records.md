# p02 — Company and branch records

Date: 2026-10-03. Piece: p02-tenancy. Status: accepted.

## Decision

- **Company**: code (2–20 capital letters, digits or hyphens, unique in the workspace, typed in
  any case), legal names in English and Arabic (both required), trade licence number and
  licensing authority, tax registration number (digits only, stored, no tax logic), base
  currency (ISO 4217 code, default AED, checked against the currencies .NET knows), fiscal year
  start (month and a day that exists in every year), address (lines, city, emirate when the
  country is AE, P.O. box, ISO 3166 country, Arabic address for printed documents), phone,
  e-mail, website, logo, active flag.
- **Branch**: belongs to one company for life (the composite key `(tenant, company, id)` lets
  access rows and workplaces prove the branch's company). Code unique within the company, names
  in English and Arabic, address and contact, active flag.
- **No deletes.** Companies and branches are deactivated, never deleted. Later documents will
  reference them, and the audit trail keeps their history.
- **Logo**: PNG, JPEG or WebP only (no SVG, so no script), at most 512 KB. The bytes must match
  the declared type. It is stored in the row (`bytea`) and served with `Content-Disposition:
  inline`, `nosniff` and an ETag. The audit trigger leaves the bytes out (`ignore: ["logo"]`)
  and records `logo_hash` (SHA-256) instead. The logo endpoints are a file surface with their own
  isolation probe.
- **Codes are unique per workspace**, not per company scope. An administrator limited to
  company X can learn that a code is taken in the workspace (409), and nothing else about it.
  This is the same trade-off as a unique e-mail within a workspace.
- **TRN format.** Only "digits, at most 20" is checked. Whether to enforce the Federal Tax
  Authority's exact TRN format is a statutory rule. It needs the owner's sign-off and a sourced
  entry in `docs/compliance/` (listed as a human gate), so it is not guessed here.

## Why

These are the fields a UAE trading or manufacturing company prints on invoices and needs for
registration, in both languages from the first screen. Deactivation instead of deletion keeps
the ledger rules (traceability) possible for the modules that will post against them.
