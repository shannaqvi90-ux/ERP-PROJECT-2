# p04 — Number, amount and date formatting; the print layout base

Date: 2026-10-03. Piece: p04-shell. Status: accepted.

## Decision

- **One formatter** (`web/src/kernel/format.ts`, exposed as `useI18n().format`): `number`,
  `decimal`, `amount(value, currency)`, `percent`, `date`, `dateTime`, `time`, `digits`. Locale
  `en-AE` or `ar-AE`, Gregorian calendar, the user's digits on Arabic screens.
- **Money stays decimal** (CLAUDE.md rule 2): amounts arrive from the API as decimal strings and are
  formatted from the string (`Intl.NumberFormat.format("12345678901234567.89")`, exact decimal
  formatting), never through a JavaScript number. Non-decimal input throws. Amounts show the ISO
  code at the currency's minor units (AED 2, KWD/OMR/BHD 3), keeping any extra precision.
- A gate fails when screen code calls `toLocaleString`, `toFixed` or `new Intl.NumberFormat/
  DateTimeFormat` directly; only the formatter and the message formatter (which receives the
  formatter's locale) may.
- **Print layout base** (`web/src/kernel/print.tsx`, `PrintDocument`): a document has its own
  language and direction (`lang`/`dir` on its root), so an Arabic document prints right to left
  from an English screen; letterhead (issuer, title), facts, body, footer (printed at/by). The print
  stylesheet sets A4 pages with margins and a page counter (`@page` with `@bottom-center`), hides
  the app's chrome (top bar, pane, status bar, breadcrumbs, pagers, search fields) and prints any
  screen with a small letterhead (workspace, printed by, printed at). Reports (p06) render their
  documents inside `PrintDocument`.

## Why

- Formatting in one place is the only way a digit preference can reach every screen.
- Printing from the browser keeps one rendering path (and the Arabic shaping of the browser) for
  screen and paper; p06 can add server-side PDF later on the same markup.
