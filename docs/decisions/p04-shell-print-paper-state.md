# p04 — Paper carries no interaction state; the shell's language switch refreshes the screens showing the user's row

Date: 2026-10-18. Piece: p04-shell, round 11. Status: accepted.

## The print base removes every interaction state from paper

The round-10 critic printed the Arabic users list after reaching a column header with Tab: the
printout carried the 2 px blue focus ring round "الاسم". The shell is keyboard-first, so a user
nearly always leaves the focus on something when they press Ctrl+P or choose "Print this screen",
and this print base is the one every later printed document (invoices, delivery notes,
statements) uses.

Decision: one block in the shared print stylesheet (`web/src/styles.css`, under `@media print`):

- `outline: none` and a transparent caret on every element and its `::before`/`::after`;
- `box-shadow: none` on `:focus` and `:focus-visible`;
- no background or inset marker on the states a list shows: the row under the pointer, the current
  row of a focused grid (`is-active`, rows and groups), selected rows (`is-selected`,
  `aria-selected` in the tenancy grids, `.id-selected`) and the search's top hit (`is-tophit`).

Nothing is blurred and no script runs on `beforeprint`: the browser prints a copy, so the focus is
where it was when the print dialog closes, and the ring shows again on screen.

Alternatives rejected:

- Blurring the active element on `beforeprint` and restoring it on `afterprint`: the restore is
  not reliable (the dialog may be cancelled, a re-render may replace the element), and a keyboard
  user would lose their place.
- Listing each module's focus selectors under print: every new module's focus or hover style would
  leak again until someone noticed. The universal `outline: none` covers styles not written yet;
  the test below covers any other property.

### The check is generic, not a list of selectors

`tests/e2e/specs/print-paper.spec.ts`, in English and in Arabic, visits every screen of the
navigation (8 today) and, on each, takes the clean printout (nothing focused, nothing hovered, no
selection; it waits until the printed text stops changing so a panel still filling in is not a
difference). Then for every Tab stop of the main region (up to 30 per screen), for the grid's
current row, a row selected with Space, the pointer on a row, and every Tab stop of a record opened
beside a list (up to 12 per record), it prints again and compares, element by element, the outline,
shadow, background, border colours and widths, text colour, decoration, opacity and visibility of
everything that prints, the `::before`/`::after` of each too, and the printed text (digits folded,
so the printed-at minute may turn). Any difference fails with the element and the property. It
also checks that the focus is on the same element while printing and after, for the elements that
print. (Emulated print media restyles the live page, so Chromium drops the focus from an element
that does not print; the test gives that element the focus back, as a real print leaves it, and
judges the focus only on elements that print, which are the ones a ring could show on.) A second
test is the critic's own path: Arabic users list, the Name header focused, printed: no outline, no
shadow, still focused, and the ring back on screen afterwards.

Planted regressions the check caught on the running stack (each a stylesheet added by the test
copy only): a print-media `:focus-visible` outline (46 findings in English, the same in Arabic,
the grid itself and every sort header), the current row's background (5 list screens), the row
under the pointer (5), a selected row's inset marker (5), a focused field's border (7, the
workspace and My account forms), a `::after` mark on a focused sort header (16), and a focused
field's colour in an open record panel (19 in each language). With the fix and no plant: 0 in
both languages, 146 Tab stops per language of which 40 in open records, three runs in a row.

Processor time this adds to the verify: the two walks take about 25 s of wall time each on the
2,000-user verify stack, the critic-path test about 3 s; in processor time (the browser, the
runner and the server answering) about 80 s in all.

## The language switch tells the screens that show the user's own row

The round-10 critic switched to Arabic with Alt+L on the users list: the signed-in user's own
Language cell still said "الإنجليزية" (English) 7 s later, until a reload. The switch saves the
user's own row (`PUT /api/identity/me/preferences`), but nothing told the list on show. Looking
further, an open record of one's own user had the same problem in a worse form: it kept the old
language and the old version, so its next save was refused as "changed by someone else".

Decision:

- `web/src/kernel/recordEvents.ts`: `announceRecordChanged({ record, id })` and
  `useRecordChanged(record, handler)`, a window event in this page only. It stores and sends
  nothing, and its detail is the record's kind and id alone (the listening screen already knows
  both). It is a shared extension point any module can use when something other than a record's
  own screen changes that record.
- `kernel/preferences.ts` announces `identity.user` with the user's id once the server has the
  change (not when it was refused or is still pending).
- The users screen refreshes its rows on that announcement; an open user record whose id it is
  re-reads the record and takes it with the form kernel's new `rebase`: the form shows the new
  values and holds the new version, and a field the user has changed keeps what they typed (and
  stays a change). `rebase` is additive to `useRecordForm`; `adopt` is unchanged.
- My account's language choice now saves through the same kernel function as the shell's switch,
  so it is safe across an immediate reload and announced the same way.

Tests: kernel unit tests for the announcement and `rebase`; screen tests on the users list (Alt+L
with one's own record open: the row reads again and says "العربية", the form says Arabic with no
unsaved change, an edit then saves with the new version; and a Language being edited by hand is
kept when the digits switch arrives, and saved with the new version); and an end-to-end test on the
stack (own row and record in Arabic within 3 s of Alt+L). Removing either listener fails them.

## Comparison margins (critic's note on thin modelled-human margins)

- **new-user-keyboard**: a third path for ours, the palette: Ctrl+K > "n" > Enter. A one-letter
  query puts the screen's and modules' actions first, and "New user" is the first one offered, so
  the form opens with the e-mail field focused from the screen shown after sign-in, with no list in
  between. Measured on this branch's stack against the shared rig: 3 steps, 4 keys, 0.12 s machine,
  2.87 s modelled, against Odoo's 4 steps, 10 keys, 2.23 s machine, 5.90 s modelled. The G > U >
  Alt+N path (5.57 s) and the navigation path stay; all three win every metric.
- **reach-screen-keyboard**: G > U stays the winning path (2 steps, 2 keys, 3.66 s modelled against
  Odoo's 3 steps, 8 keys, 3.99 s). Tried: the palette, Ctrl+K > "us" > Enter, is 3.15 s modelled but
  3 steps, equal to Odoo's 3, so as a whole path it does not win; Alt+M > Enter is 2 steps but
  3 keys and 4.18 s. A path of one step would need a key chord per screen, which does not scale to
  the screens later modules add and which no other shell screen uses; the go-to keys are the one
  scheme for every screen. The harness models G then U as two mental units (its continuation rule
  joins only repeats of the same navigation key); the harness's rules are not this piece's to
  change. Every metric of the G > U path still wins.

## This round's verify

`./erp verify` on this branch (integration branch merged at be1042cd) through the verify slot:
passed in 4,164 s, processor time 10,338 s against the 10,500 s maximum (dotnet 7,862, web 2,009,
e2e 428, timing 39), under a load that peaked at 74 from other agents. Counts: .NET 656, web unit
512, end-to-end 110, compare 415, build 6, none failed or skipped. The ratchet minimums for web unit
(504 to 512), end-to-end (106 to 110) and .NET (650 to 656) are raised to these counts.
