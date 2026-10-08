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
- **Every screen prints through the base** (round 2): the shell wraps the open screen in
  `<PrintDocument screen …>`. On screen the wrapper adds nothing visible (no box, no width, no
  second heading: the letterhead title is not an `h1`); on paper the screen gets the base's
  letterhead (workspace, screen name) and footer (printed by, printed at). The printed time is
  stamped on `beforeprint`, so it is the moment of printing, not of opening the screen. One
  letterhead implementation for screens and documents.

## Why

- Formatting in one place is the only way a digit preference can reach every screen.
- Printing from the browser keeps one rendering path (and the Arabic shaping of the browser) for
  screen and paper; p06 can add server-side PDF later on the same markup.

## Round 3: a printed screen carries no interactive chrome

Printing the users list in Arabic kept the "New user", "Columns" and "View" buttons, the row
checkboxes, sort arrows and the keyboard hint line. The print stylesheet now hides, inside a printed
screen, every button except column-title sort buttons (which print as plain headings), menus, the
list search, filter-chip remove buttons, the selection bar, unchecked checkboxes and every checkbox
in a list grid, sort marks, key hints (`kbd`, elements with `aria-keyshortcuts`, the list's hint
line) and anything a module marks `.no-print`. Form fields print as their values (no borders or
backgrounds). An end-to-end test prints the users list in Arabic and requires no visible button,
checkbox, sort mark or hint, with the Arabic column titles still printed.

## Round 4: printing a screen and printing a list are two paths, and the printout says so

The critic printed the 100,006-user list from the browser and got one page of the ~20 rows on
screen under a heading of 100,006 users. Two paths now exist, each with its own job:

- **Ctrl+P / the palette's Print** prints the screen as it stands, through this base. For a list
  that is the rows on screen, so the printout carries a paper-only line saying which rows it holds
  of how many (`lists.print.partial`).
- **Print or export** (p06's report engine, `/api/reports/lists/{key}`) prints or exports the whole
  list as filtered, sorted and grouped, as PDF (English or Arabic), CSV or Excel. When a list has
  that menu, the paper-only line names it as the way to print every row
  (`lists.print.partialReport`), instead of the shell growing a second whole-list printer.

The shell does not page through a large list to print it: the report engine owns whole-list output
and its limits, and one place renders a list's rows on paper for every module.
