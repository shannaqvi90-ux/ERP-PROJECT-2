# p06 — The screen permission gate by keyboard, and round 3's report and form choices

Date: 2026-10-07. Piece: p06-form-report. Status: accepted.

## Context

Critic p06 round 2 planted W2 in the record form kernel: `forms.save` left enabled for a read-only
user and `save()` no longer checking `canEdit`. A read-only user's Ctrl+S then sent a PUT, and every
gate passed, because the screen gates only looked at the controls drawn. The product is
keyboard-first: its shortcuts are its main controls.

## Decisions

### G2 on screen: sweep every key, not the shortcuts we know of

- `web/src/test/keySweep.ts` presses every key a keyboard has (letters, digits, named keys, F1-F12,
  punctuation), alone and with Ctrl, Alt, Shift, Ctrl+Shift and Alt+Shift (Ctrl+Alt is AltGr), on
  each focus target of a screen whose user lacks a write permission. It accepts whatever a key
  opened (the focused or primary button of a dialog or menu, as Enter would), then activates every
  control the keyboard reaches and every control each one reveals (a tab's page, a menu's items).
  It reports every request other than a read (the user's own settings, sign-out and personal list
  views excepted) and, for list screens, any new-record form offered to a user who may not create.
  It does not need to know which shortcuts exist, so a shortcut added later is pressed too.
- Screens are swept as shown and in extra states (every list row chosen, where bulk actions appear).
- Each sweep file has a control: the same sweep for a user who may write finds the writes (Ctrl+S,
  Ctrl+Enter, the bulk deactivation), so a blind sweep fails.
- `keySweepCoverage.test.ts`: every module whose screens send writes must have a
  `keyboard.test.tsx` that calls `sweepKeys`; a new module cannot skip the gate.
- `scripts/forms-plant-self-test.mjs` (run by `./erp verify`) plants 17 faults — W1 and W2, the same
  fault through Ctrl+Enter, the shortcut sheet, Escape's save-and-close and new records, and
  screen-level faults (plain n and Alt+N opening new records, bulk actions without their permission,
  module forms editable, sign-out-everywhere and unblock offered) — and requires the gates to fail
  on each. In a planted copy a sweep may stop at its first find (`ERP_SWEEP_FIRST_FIND=1`); the
  unplanted control and the sweeps' own controls always sweep everything.
- A sweep test has its own 60 s limit (`sweepTimeLimit`): it presses some thousands of keys (2 to
  6 s each on a quiet machine), not one interaction. It is not a load allowance.

### Leaving unsaved changes asks the same question everywhere

`requestLeave(proceed)` replaces the synchronous `confirmLeave()` on every in-app way out (menu,
palette, another record, closing the panel): the record form's own dialog asks (save and close,
discard, keep editing) and then leaves. The browser's confirmation is left for reloads and closing
the tab, which only the browser can ask about.

### Exports hold whole main lists and say when they do not

CSV and XLSX exports hold up to 200,000 rows (twice the 100,000-record main lists), written row by
row into the output. A file cut at its limit ends with the document's row-count line in its
language (CSV: last line; XLSX: under the table, outside the filter and the totals). Streaming the
whole response was not chosen: the document (groups, totals, names) is built first either way, and
200,000 rows fit in memory; a background export job belongs to p12/p14.

### Printed text that reads back

- Sort order is printed in words ("Code, descending"), not arrows the embedded fonts lack; the
  report render gate fails on any printed character no embedded font draws.
- Each run's letters are written as one TJ string from one point (offsets and advances as TJ
  adjustments); dots and marks follow as artifacts; a combining mark's character goes with its
  letter; a right-to-left line writes its runs from its right end. Readers that take text in the
  order written (pdf.js) then get whole words in reading order; `/ActualText` per right-to-left run
  stays for readers that use it (Poppler, Acrobat).

### Flag columns may name their values

A boolean list column may carry the choices `true` and `false` (exactly that pair) to name its
values; screens, filters and printed documents show those names (a role's type: System or Custom)
in place of Yes and No. Columns of other records' ids printed as counts align like numbers.
