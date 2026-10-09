# p04 — A wrapped screen keeps its own table layout; checkboxes are their own size

Date: 2026-10-07. Piece: p04-shell, round 5. Status: accepted.

## Problem

The round-4 critic saw the users list's columns shift from row to row, in Arabic and English,
on rows with an empty cell ("not signed in yet", no roles). The list grid itself was sound: every
row has the same column template and width. Two rules from the shell's own stylesheet were the
cause:

- The print layout base (`kernel/print.tsx`) wraps every live screen in `.print-document
  .print-document-screen` so that a screen prints with a letterhead. Its document table rules
  (`.print-document th, .print-document td`: 3 px block padding and a bottom line) therefore hit
  every table cell on screen, the list grid included. The list grid lays each row out as a 28 px
  CSS grid row with one line under the row; a cell with text grew to 27 px plus its own line, and an
  empty cell shrank to 7 px, centred, with its line at another height.
- The global field rule (`input { height: 30px }`) also made every checkbox 30 px tall, so the
  selection cell was 45 px tall in a 28 px row.

## Decision

- Document table rules apply to documents only (`.print-document:not(.print-document-screen)`). A
  wrapped screen draws its tables as the screen does, on screen and on paper; the screen's own
  stylesheet (lists, `.grid`) owns them.
- Checkboxes and radio buttons take their natural size (`height: auto; padding: 0`).
- The list's selection cell centres its box and never shows an overflow mark (kernel/lists).

## Test

`tests/e2e/specs/shell.spec.ts`, "every list screen draws each row on one line": on every list
screen in the navigation, in English and Arabic, on screen and in print media, every cell of the
header and of each row in view lies inside its row and draws no line of its own. It reported 102
findings on the previous build and none after the change.

## Alternatives considered

- *Reset padding and borders in the list stylesheet.* It would fix lists only; any other screen
  table inside the wrapper (role matrix, company form tables) would keep the document's cell rules
  on screen, and the print layout base promises to add nothing visible on screen.
