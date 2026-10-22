# p05 — a list beside an open record shows only whole columns (round 14)

Date: 2026-10-22. Piece: p05-list-search (round 14). Status: accepted.

## Context

Two findings routed to p05 by other pieces' critics:

- p04 round 10: with a record open on the Arabic users list at 1440 px, the grid ran past its
  frame. In a right-to-left list the row's end is on the left, so the leftmost columns were cut
  without an ellipsis: the "Last sign-in" header and its dates were cut, and Roles could not be
  seen at all.
- p03 rounds 9 to 13: with a record open at 1366 px, the English users list showed "Langu…" for
  the Language header.

Measured on this branch before the change (2,005 users, admin and Arabic admin):

| Window | Language | List body | List frame | Record | Sideways overflow |
|---|---|---|---|---|---|
| 1366 | en | 1126 | 542 | 574 | 203 px, "Language" cut by 10 px |
| 1440 | en | 1200 | 614 | 576 | 131 px |
| 1366 | ar | 1126 | 576 | 540 | 215 px |
| 1440 | ar | 1200 | 614 | 576 | 177 px |

The list's columns need about 745 px (English) and 800 px (Arabic). The record form opened at
48% of the body, and the list kept the rest whether its columns fitted or not. The header
estimate (0.56 rem a character plus padding) was a few pixels short of the rendered "Language"
with its column menu.

## Decision

1. **The form opens no wider than the list can spare** (`openingShare` in `recordWidth.ts`). With
   no width chosen by the user, the form opens at the standard 48%, or at the share that leaves
   the list every visible column whole, whichever is narrower, never under the form's 380 px floor.
   A form whose content is wider than that still grows to fit it (unchanged), and the user's own
   choice (splitter, arrows, Home/End, Alt+W) still wins.
2. **Only whole columns are shown beside an open record** (`columnFit.ts`). When the frame cannot
   hold every column at its minimum, the list keeps its leading columns in the user's order and
   ends the row with a marker, "+2", whose accessible name and tooltip name the columns waiting
   ("2 more columns beside the open record: Last sign-in, Roles"). Nothing changes in the view
   itself: chosen columns, saved views, copies, prints and exports keep every column, and closing
   the record brings them back. When narrowing the form would show more columns, the marker is a
   button that does so (it sets the user's width for this list). Otherwise it is a label. Grouped
   lists and lists with no record open are unchanged.
3. **Headers and fixed-shape values are measured where they are cut.** After each layout, a header
   label that is cut asks for the room it lacks, kept per column and label, so each language's
   own text counts, and so does a sort mark. A date, time, number or amount cell that is cut does
   the same, per column and language. Columns only grow, up to the 16 rem cap. Free text (names,
   e-mails) is still cut at its own end with an ellipsis. Fonts finishing loading trigger a new
   measurement.
4. A change of size (the form growing to fit its content, a scroll bar appearing) is laid out
   again inside the ResizeObserver callback (`flushSync`), before the browser paints. The form
   measures its content the same way: in a layout effect, and in its observers' callbacks, which
   re-render at once. That replaces the old effect that waited for the next animation frame. The
   share each list's forms needed is remembered for the page, so the next record of that list
   opens at that width. Otherwise it would open narrower while it loads and widen once drawn.
5. **Nothing is measured while the page is laid out for paper** (`printing()`, the print media
   query). A printout is the screen as it stands. The first builder verify of this round found
   that emulated print media resized the frame, the observers re-measured the paper layout, and
   the users and companies lists then printed different columns depending on when the print was
   taken (print-paper.spec.ts, "every screen prints as it does with none of it (en)").

Also in this round: "/" typed in the search box when it is empty, or when its whole text is
selected, keeps the cursor in the box instead of starting the search with a "/". The status bar
says "/ search", and after G U the cursor is already in the box (critic p03 round 13). Inside a
search being typed, "/" is still text.

After the change (same data):

| Window | Language | Record | List frame | Columns shown | Overflow, cut headers, cut dates |
|---|---|---|---|---|---|
| 1366 | en | 574 (content needs it) | 542 | Name, E-mail, Language, +3 | none |
| 1440 | en | 576 (content needs it) | 614 | Name, E-mail, Language, Status, +2 | none |
| 1366 | ar | 405 | 711 | Name, E-mail, Language, Status, +2 | none |
| 1440 | ar | 408 | 782 | all but Roles, +1 | none |
| 1920 | en, ar | 806 | 864 | all six | none |

After the third merge of the integration branch (p02 and p04 round 11 work, including the
user form as it now stands), measured on the merged build:

| Window | Language | Record | List frame | Columns shown |
|---|---|---|---|---|
| 1366 | en | 383 | 733 | Name, E-mail, Language, Status, +2 |
| 1366 | ar | 383 | 733 | all but Roles, +1 |
| 1440 | en, ar | 396 | 794 | all six |
| 1920 | en, ar | 806 | 864 | all six |

On paper (p04 round 11's print layout), every visible column prints at its share of the page,
including the columns waiting beside an open record. Those columns stay in the row and are
hidden on screen only, and the marker does not print.

The table before this note was measured before that merge.

The English user form needs about 560 px because its "Roles in one company" table holds the
English company names (identity module, p03). That is why English shows fewer columns than
Arabic beside the record. It is the form's own content, not the list's.

## Alternatives rejected

- **Keep the sideways scroll.** This is what the critics found. In Arabic the cut side is the
  left, with no ellipsis, and a column can be off screen with nothing to say it exists.
- **Squeeze every column below its minimum.** Everything is "visible" but cut, which is the
  "Langu…" finding again, everywhere.
- **Always open the form at a fixed narrow width.** Wide forms (the role matrix, "What they can
  do") were cut that way before (p03 rounds 5 to 7). They keep growing to fit.
- **Estimate label widths more generously.** That fixes one label in one font and wastes room in
  every other. Measuring the rendered header is exact in both languages.

## Checks

- Unit: `columnFit.test.ts` (which columns fit, the marker's room, at least one column, unknown
  width), `recordWidth.test.ts` (opening share, room for the list), `ListView.test.tsx` ("/" in
  the search box).
- End to end (`lists.spec.ts`), English and Arabic at 1366 and 1440 px with a record open: after
  the layout settles, no header label is cut, no date or amount is cut, no header lies outside the
  frame and the frame does not scroll sideways. The shown columns are the leading ones in order,
  and the marker counts and names every waiting column. Where the marker is a button, pressing it
  shows more columns, still whole. Closing the record brings every column back. With the widest
  record (End on the splitter), the marker is always a button, and it narrows the record. A
  separate test covers "/" with the cursor already in the search box.
- Planted regressions, run against a local stack: (a) never hiding columns failed both languages
  at 1366 px; (b) skipping the measurement failed English on "Language: header cut (85 > 75)"
  and Arabic on the sign-in dates "value cut (160 > 155)".

## Cost

These checks add three end-to-end tests of about 4 s each, so roughly 12 s of browser time and
about 15 to 20 processor seconds in a verify. Six unit tests add well under a second. The
layout effect that measures headers runs only when the list's columns, sort, search, filters,
language, frame width or rows change. It reads one header row and the rows on screen.

Builder verifies this round, all run beside other agents' verifies:

| Run | Slot taken (UTC) | Result | Processor seconds (.NET, web, e2e) |
|---|---|---|---|
| a | 2026-10-21 22:52 | money gate (a `parseFloat`), print-paper (en) and 7 client-isolation unit tests failed; the client-isolation journey timed out at 30 s under load, as it also did in p04's round-12 builder verify | 7,830, 165 (web stopped early), 212 |
| b | 2026-10-22 01:05 | only the G1 client module-state gate failed (an unscoped `Map`, fixed with `identityScoped()`); web and e2e passed | 8,081, 2,509, 450 = 11,040 under load |
| c | 2026-10-22 03:31 | .NET and e2e passed; one comparison-harness control failed under load (causation.test.mjs:710, "wsask", a style recalculation 1.97 s before the clock), so the timing stage did not run | 5,134, 1,935, 378 = 7,447 |

The integrator's own run alone is what counts for the 10,500 s maximum. For comparison, p00's
round-13 integrator run took 6,638 s on the same suite without this round's tests.

## Merges

This round merged the integration branch twice. The second merge brought p00 round 13, which
made one shared outside-process source rule out of p00's rule and p05's external-state rule.
`tests/Gates/tenant-bypass-sources.txt` takes p00's version, whose outside-process entries
review ClientAddresses.cs (2 uses) and ReportEndpoints.cs (5 uses: the FileStream return type
is counted too). So this round's own external-state entries for those files were dropped, not
kept beside them.
