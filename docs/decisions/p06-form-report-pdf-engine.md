# p06 — Our own PDF writer, with HarfBuzz for Arabic shaping

Date: 2026-10-04. Piece: p06-form-report. Status: accepted.

## Decision

PDFs are written by the reports module itself (`src/Modules/Reports/Erp.Modules.Reports/Pdf/`):

- **Fonts.** Noto Sans (Latin, Latin Extended) and Noto Sans Arabic, regular and bold, as WOFF
  files embedded in the assembly (SIL OFL-1.1, accepted by the owner on 2026-10-03, needs-human
  #5; each file reviewed with its hash in `tests/Gates/font-licences.txt`, licence text beside it).
  WOFF is unpacked to an sfnt in memory and embedded as a Type0/CIDFontType2 font with
  Identity-H encoding (whole until 2026-10-08; since then a subset of the glyphs each document
  draws, see p06-form-report-keyboard-gate-and-round-3.md).
- **Shaping.** HarfBuzzSharp (MIT; HarfBuzz itself is under the "Old MIT" licence) shapes every run:
  Arabic joining forms, ligatures (lam-alef), marks and kerning. Each run picks the font that has
  its characters (Arabic font for Arabic, Latin for the rest, Arabic-Indic digits from the Arabic
  font).
- **Direction.** A subset of the Unicode bidirectional algorithm (UAX #9: strong types, numbers
  and neutrals, paragraph direction from the document language) orders runs on each line; an
  Arabic document mirrors the page: right-aligned text, columns from the right, numbers kept
  left-to-right inside.
- **Text you can copy and search.** Every glyph run carries a `ToUnicode` map; right-to-left runs
  are wrapped in `/ActualText` with the logical text, so copying or searching an Arabic PDF gives
  the words in reading order. Combining marks are drawn as artifacts so text extraction does not
  duplicate them.
- **Layout.** A4, portrait, or landscape when the columns need it; column widths from the content;
  wrapping by shaped width; the table header repeated on every page; group headings kept with
  their first rows; "page x of y" and the print stamp on every page. All geometry is `decimal`
  (the money rule's gate forbids `float` and `double` in the product).

## Why

The bar is an Arabic document a UAE accountant would hand to a vendor, from a small deployable.
The candidates under an allowed licence fail one of: correct Arabic shaping and direction, size, or
the licence itself:

| Option | Why not |
|---|---|
| QuestPDF | Community licence is free only under a revenue threshold; paid otherwise (not MIT/Apache/BSD). |
| iText 7 / iTextSharp | AGPL or paid. |
| PdfSharp / MigraDoc (MIT) | No complex-script shaping: Arabic comes out as isolated, left-to-right letters. |
| Headless Chromium (Playwright, Puppeteer Sharp) | Correct shaping, but a browser in the deployable (hundreds of MB, a process per print, a sandbox to manage) and print CSS for repeated headers and page numbers. |
| wkhtmltopdf | Archived, LGPL, old Qt WebKit. |

HarfBuzz is the shaping engine browsers and LibreOffice use; with it, the remaining work (font
embedding, bidi ordering, a table layout) is a few hundred lines we test directly: the rendering
tests decode our PDFs with PdfPig (Apache-2.0, tests only) and check Arabic words in reading
order, page counts, headers repeated and landscape choice.

## Consequences

- The deployable carries HarfBuzz's native library (`HarfBuzzSharp.NativeAssets.Linux`, MIT).
- Glyph coverage is the bundled fonts': Latin, Latin Extended, Arabic. A character outside them
  prints as the font's missing glyph; adding a script means adding its Noto font and listing it.
- Bidi is a subset: explicit embedding controls (LRE, RLO …) are not interpreted. Business text
  rarely carries them; the full algorithm can replace `Bidi.cs` behind the same call.

## Round 2 (2026-10-06): text a reader extracts, telephone numbers, the header

- **Character ids, not glyph ids.** Arabic letters share glyphs: س and ش, ر and ز, medial ب and
  ن are one base glyph with different dots, the dots drawn as marks; ا and إ likewise. A ToUnicode
  map keyed by glyph gives each shared glyph the text of whichever letter came first, so copy,
  search and screen readers read الإشم for الاسم (critic round 1). The writer now hands out one
  character id per distinct pair of glyph and text, maps ids to glyphs with a `/CIDToGIDMap`
  stream, and keys widths and the ToUnicode map by id. A rendering test prints the critic's words
  on a page with no other Arabic and requires every Arabic letter to extract exactly as often as
  it was printed (the old writer fails it).
- **Telephone numbers read left to right.** Under the algorithm alone, digit groups separated by
  spaces are separate runs, so an Arabic line orders them right to left ("7810 555 2 971+"),
  while the screen shows the number as typed. The bidi pass treats a telephone-like span (an
  optional plus, then digit groups joined by spaces, hyphens or brackets, at least seven digits)
  as one left-to-right unit. Dates with times (slashes, colons) and amounts (commas, points) do not
  match and keep the algorithm's order.
- **Header.** `%PDF-1.7` (round 1 wrote `%PDF-1.7m`, which pdf.js warns about and strict
  validators refuse).
