# p02 — A company's English legal name is required

Date: 2026-10-09. Piece: p02-tenancy. Status: accepted (round 7).

## Context

The p06 round 3 critic (routed to p02) saved a company whose English legal name was empty because
an Arabic legal name was given: the company rule was "a name in English, in Arabic, or both". The
English legal name is what identifies the company everywhere the screens and documents are in
English: lists and the switcher, codes made from the name, English print-outs of the company
profile and of every later module's documents. An empty one prints a document with no legal name.

## Decision

- Companies: the English legal name is required on create and on change (error code
  `tenancyLegalNameEn`, in English and Arabic); blank or white-space-only is refused and nothing is
  saved. OpenAPI documents it with a minimum length of 1; the form marks the field required.
- The Arabic legal name stays optional. Whether a registered Arabic legal name must appear on
  printed company documents is a statutory question waiting for the owner (needs-human #9), so the
  product does not decide it; the form keeps its hint that Arabic documents print the English name
  when the Arabic one is missing.
- Branches keep "English, Arabic or both": a branch is not a legal entity and is often known only
  by a local name.
- The same finding says "12345" passes as a tax registration number. The TRN format is a statutory
  rule (CLAUDE.md rule 8) already waiting for the owner (needs-human #10); it must come from a
  current Federal Tax Authority publication recorded in `docs/compliance/` with owner sign-off, so it
  is not changed here from memory. The field keeps its storage-only rule (digits, at most 20).

## Round 8: the database holds the same line

The API refused the empty English legal name from round 7, but the table's check constraint
`ck_companies_legal_name` still read `legal_name_en <> '' OR legal_name_ar <> ''`, so any other
writer (a seeder, an operator command, a later module, a hand-made SQL fix) could still store an
empty or blank English legal name next to an Arabic one. Migration
`CompanyEnglishLegalNameRequired` replaces it with `btrim(legal_name_en) <> ''`. Existing rows
already pass: the seeders and provisioning always give an English name and the API has refused a
blank one since round 7. A database holding a blank English legal name from before round 7 (a
developer's old demo volume) fails this migration; `./erp down --volumes` starts it again (no
production data exists). `CompanyTests` checks that an UPDATE to an empty and to a blank English
legal name is refused with a check violation naming the constraint.

## Processor time

One extra module test path (a few requests and two refused UPDATE statements in an existing test)
and one web assertion: well under a second.
