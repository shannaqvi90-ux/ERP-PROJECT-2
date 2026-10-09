# p04 — The isolation gates attack every documented value, not only the first

Date: 2026-10-07. Piece: p04-shell, round 5. Status: accepted.

## Problem

The round-4 critic planted a leak on the shell's own endpoint (plant L1): `PUT
/api/identity/me/preferences` kept the last Arabic-Indic-digits caller's e-mail in a temporary file
and appended it to the next such caller's display name. Every G1 gate passed. The write-after-write
phase and the non-interference check build each write body from documented values, and
`OpenApiDocument.BuildBody(useDocumentedValues)` takes the first value of an enumeration, so no
tenant ever sent `language: "ar"` or `numerals: "arab"`. The code behind every other value of every
enumerated field (the Arabic side of the shell, and any module whose behaviour branches on an enum)
was never run by either tenant, so a leak there could not show.

## Decision

1. **Variants of every write body** (`TenantActivity.VariantsOf`, `OpenApiDocument.EnumLeaves` and
   `SetLeaf`): for every write endpoint, every leaf whose document publishes allowed values (an
   `enum`, which `AllowedTextValues` fields and C# enumerations both become), nested objects and
   array items included, yields one variant per allowed value; when a body has more than one such
   field, one more variant per value index sets all of them together (Arabic language with
   Arabic-Indic digits). A variant is applied after the record's own values (the edit-and-save
   template), so it always reaches the handler.
2. **Write after write, per variant** (`G1HttpIsolationTests.WritePairsPhaseAsync`): tenant B and
   tenant A send the same variant back to back (cookie and bearer), tenant B writes it once more
   after A, and both read everything afterwards. Answers are judged for the other tenant's markers
   as before. A variant that never succeeds on both sides with its value in the body is a blind
   spot and fails the gate, as an unsuccessful own write already did. The default body is written
   last, which puts both tenants' records back to their first values.
3. **Non-interference for writes** (`NonInterference.CompareWritesAsync`): every write on an
   existing record (PUT, PATCH, POST on a record's route), with its default body and every variant,
   is compared like a read: the judged tenant's answer in the shared process right after the other
   tenant made the same write, against its answer in a fresh process only it has used (written
   twice there, so an answer that changes from one write to the next is reported as unstable, not
   as a finding). Only `version` fields (a row's concurrency token, which every save changes),
   times and trace ids are left out of the comparison. This catches state that hands on only a
   number (leaky bug 46).
4. **Every tenant activity warms up with every variant** (`TenantActivity.WriteAsync`), and
   enumerated query parameters get their published values as well as free text, both in tenant
   B's reads and in the non-interference requests.
5. **Self-tests**: the leaky module carries plant L1's exact shape (bug 45, a temporary file that
   the process-state gate cannot see) and a number-only Arabic leak (bug 46). The HTTP self-test
   requires bug 45 to be caught in both directions and only with `"arab"`; the non-interference
   self-test requires both to be caught and only with `"ar"` / `"arab"`.
6. **Ratchet**: `g1.enumValuesAttacked`, `g1.enumVariantWritePairs`,
   `g1.writeNonInterferenceComparisons`, `g1.writeNonInterferenceEndpoints` and
   `g1.writeNonInterferenceVariants` hold the counts measured on the product.

## Alternatives considered

- *Hard-code "ar" and "arab" next to "en" and "latn".* Covers this plant only; any other module
  whose code branches on an enum value would stay unattacked.
- *Every combination of every value.* Grows multiplicatively (company and branch bodies carry
  emirates and other choices); one value at a time plus all-at-index-n reaches every value's code
  path with both tenants at a fraction of the cost.

## What the stricter gate found at once

Its first run on the product hung in the reports isolation probe: the variant writes had given
each tenant's administrator some sixty companies, and `GET /api/reports/lists/tenancy.access` as a
PDF never finished, in either language, while the server kept two cores busy after the client
gave up. The access list's companies cell was printed as the raw JSON of the list (one "word" of
thousands of characters), and the PDF text wrapper re-shaped the whole remainder for every
character it removed, with a per-glyph linear search inside shaping. Fixed in the report engine
and the print layout base (`TextShaper.Wrap` cuts by doubling then halving and stops at the lines
kept; cluster ends are looked up once; a cell is cut to 2,000 characters before layout; a
character wider than the line still makes progress, which looped before; a list of records in a
cell prints as their codes or names). Regression tests: `RenderingTests` (a 2,000-item unbroken
value in both scripts, a 100,000-character cell, a line narrower than one character) and
`ReportApiTests.A_list_of_records_in_a_cell_prints_as_their_codes`.

After the variants, the write-pairs phase and every warm-up write each enumerated field's first
value explicitly: an edit and save copies the other fields from the record, so a plain default
body would have kept the last variant's value (the workspace left in Arabic, say) for the phases
that follow.
