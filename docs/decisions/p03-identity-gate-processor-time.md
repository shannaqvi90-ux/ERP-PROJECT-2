# p03 — keeping ./erp verify under its processor-time maximum

Date: 2026-10-08. Piece: p03-identity (round 5). Status: accepted.

## Context

Round 4 of p03 merged cleanly and every test passed, but `./erp verify` used 9,708 processor
seconds against the ratchet's maximum `verify.cpuSeconds` of 9,000. The maximum may not be raised
(CLAUDE.md rule 9). The integration branch itself was already close to it (8,893 to 8,971 seconds
in the runs of 2026-10-07), so p03 had to take out more than its own gate work adds.

Per-process sampling of the .NET stage (the test hosts inside the verify container) showed where
the time goes: the G1 HTTP attack runs twice (`G1HttpIsolationTests` in the suite and its
self-test `self-http` against the leaky module), about 2,500 and 3,500 processor seconds; the
company attack's self-test about 740; everything else together well under 1,000.

A `perf` profile of the HTTP attack (one test, `DOTNET_PerfMapEnabled=1`, W^X off so JIT frames
resolve) put about 40% of its processor time in laying out printed PDF lists
(`PdfReportRenderer.Layout.Row` → `TextShaper.Wrap`/`Fits` → `ShapeRun`), with three avoidable
costs inside it:

- `ShapeRun` found each glyph's next cluster start with `FirstOrDefault` over all clusters
  (quadratic per run): `Enumerable.TryGetFirst` alone was 18% of the attack;
- `PdfFontFace.Scale` used decimal multiplication, division and rounding three times per glyph
  (`DecCalc.VarDecDiv` and `VarDecMul`, about 5%);
- line breaking shapes the same text several times (a word is shaped as a candidate line, then
  as a word that may not fit, then again before it is cut).

The attacks print every list hundreds of times (tenant B's concurrent reader, every route and
query value), so this cost is multiplied; in production it is the cost of every printed list.

## Decision

Make the work cheaper, never smaller: no check, plant, request, value or ratchet minimum changes.

1. `TextShaper.ShapeRun` sorts the distinct cluster starts into an array and finds the next one
   by binary search. `PdfFontFace.Scale` computes the same quotient in integers and rounds it half
   to even, which is what `Math.Round(decimal)` does. A `TextShaper` (one per rendered document,
   never shared between requests) keeps each line it has shaped for that document, keyed by the
   text, the size with its scale (8.5 and 8.50 stay apart, since the size is printed as written),
   bold and direction. Output is unchanged: the new shaper was compared with the previous one on
   3,000 random English, Arabic and mixed texts (marks, bidi controls, tabs, surrogate pairs,
   long words), and Shape and Wrap returned the same glyphs, advances, offsets and glyph texts;
   a unit test compares `Scale` with the decimal formula over every font unit from -70,000 to
   70,000 for each face. A 200-row users list rendered in 93 ms instead of 147 ms.
2. In the gate self-tests, `LeakyFixture` runs the grant-bearing record check and the grant
   escalation check once per leaky environment; the three and two self-tests that ran the same
   full check over the same environment each judge that one run, with their assertions as they
   were.
3. Earlier in this round (decisions `p03-identity-session-grants-one-statement` and
   `p03-identity-openapi-generated-once`): the session's user and grants in one statement, and
   the API description generated once per process.

Measured side by side on the same machine at the same time, the G1 HTTP attack alone:
integration branch (c25bb61) 2,522 processor seconds; this branch 1,465. Every count the attack
reports still meets its ratchet minimum (the test asserts them).

## Rejected

- Raising `verify.cpuSeconds`: weakens the bar (rule 9).
- Running the HTTP attack once in an environment holding the leaky module and judging both the
  plants and the product from that run: the real gate would then have to tell product findings
  from planted ones, which a critic could rightly call a narrower gate.
- A shaping cache shared between requests (in `PdfFonts`): process-wide state holding tenants'
  text, which the non-interference and process-state gates rightly refuse.
- Lowering the password hashing cost in test environments (PBKDF2 is about 8% of the attack):
  the tests would no longer run the product's security settings.
- Cutting a word that does not fit by binary search instead of the downward scan: widths of
  shaped prefixes are not strictly monotonic (Arabic joining forms change the previous letter),
  so the break could move; the scan stays and each shape is cheaper.
