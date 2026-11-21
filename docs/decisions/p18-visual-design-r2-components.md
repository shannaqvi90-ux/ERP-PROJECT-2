# p18 round 2: kernel components that close the round 1 verdict

Date: 2026-11-21. Piece: p18-visual-design, round 2 (stage 2, build). Reason for each choice is given with it.

## No text cut without a way to read it: one sweep, `kernel/overflowTitles.ts`

Round 1's critic found 28 list cells, the search placeholder, the section tabs and long form values cut with an
ellipsis and no title. The cause was structural: each component decided for itself whether to add a title.
One sweep now serves every screen, the kernel's and every module's. A MutationObserver (batched to one pass per
120 ms), the window resize, the density and language attributes on `<html>`, and pointer-over and focus-in on a
candidate all run it. It sets `title` to the full text on any cell, header, tab, label, button, badge, input value
or placeholder whose content is wider than its box, in either language, and removes the titles it set when the text
fits again. A title a component set itself is never replaced. Alternative rejected: a title on every cell by hand
(misses cells that modules render, and the next module's); a custom hover tooltip for every cell (an extra layer
per cell in a virtual list of 100,000 rows). The native title is what Odoo's list gives too.

Column resizing: each list header has a drag edge (`.list-resizer`, mirrors in right to left) and the column menu
has Wider, Narrower and Reset width, so the keyboard reaches it. Widths last for the visit (not stored: a stored
width would be one more device key to review under the client-state gate). The edge is not `role=separator`, which
the record splitter owns (its tests and the keyboard splitter select that role).

## Calendar, drop-down with search, tag field (`kernel/forms/`)

* `calendar.tsx`: our own month grid, keyboard first (arrows, Page Up/Down, Home/End, T, Enter, Escape), Monday
  first as the UAE work week, mirrored arrows in Arabic, range days drawn. The browser's picker was pointer-only.
  `DateInput`'s button is in the Tab order, Alt+Down opens the calendar from the field. The list filter's date range
  uses it with both ends.
* `SearchSelect.tsx`: type to narrow (Arabic and accent folding shared with the palette, now `kernel/fold.ts`),
  Up/Down/Enter/Escape. A hidden native select with the same name and value stays beside it, so forms, autofill and
  every existing test that drives a select keep working. Used by `CountryField` and by `SelectField` from 12 choices.
  Short lists (type, emirate) stay native selects on purpose: five keystrokes with a native select cannot be beaten
  by a custom list for 3 to 7 choices, and the comparison tasks count keystrokes.
* `TagField`: chips, Enter, comma or Tab adds, Backspace removes; value stays "a, b" so saved records are unchanged.

## Palette: `>` commands, `#` records, Tab for a record's actions

Same field and the same sources. `>` shows only commands (the screen's, the shell's, modules'), `#` only records
from the record sources, Tab on a record lists Open, Copy the name and the actions its source offers
(`PaletteItem.actions`, additive); Shift+Tab, Backspace on an empty field or Escape go back to the query.

## Shell

* Navigation: an icon before each name (the link's accessible name is unchanged), collapsible groups (toggle button;
  Left/Right collapse and open from a link), pinned screens (P on a link; they move to the top, not duplicated, so no
  link name occurs twice). State is under `erp.nav.<user id>`, removed with the identity (reviewed in
  tests/Gates/client-carriers.txt).
* Record command bar: icons on Save, Discard, Close, panels, Print and the previous/next arrows.
* Toasts (`ToastHost`, `toast()`), `Tooltip`, `Skeleton`, `Sparkline` in `kernel/ui.tsx`.
* Home: figures show a skeleton while they load (a dash only when the list cannot be read); "Do these first" lists
  the figures that need this user, largest first; a figure with `actPermission` counts as work only for a user who
  can act (the read-only viewer is no longer told to fix failed jobs); contacts show a 12-month area sparkline from
  twelve one-row counts of `createdAt` (the list API's own total).
* Dark is the default look (owner, G final); the system's light preference no longer decides, high contrast still
  follows the system. Light and high contrast stay one press away. design.spec.ts and theme.test.ts were rewritten
  for this (the first assertion changed from "follows the system" to "is dark"; every other assertion is kept).

## Not done in this round (stated, not hidden)

* Theme and density per user across devices: they are still device keys (`erp.theme`, `erp.density`). A per-user
  setting needs a server column, a migration and an endpoint with its gates; it was not started.
* List group-by with sticky base-currency subtotals, the 12-month trend and credit-bar columns, and the details pane
  need module data and server work (base-currency subtotals, per-row history) that no module offers yet.
* The record command bar's overflow menu: icons are in, the overflow is not (moving existing buttons behind a menu
  changes the keystroke paths the comparison tasks count).
* Sidebar "Recent": the palette already lists recent screens; a second list of the same links in the pane would
  repeat every link name.

## Cost

Web only. No server code, no endpoint, no table. Processor time added to `./erp verify`: the new unit tests
(about 30) and one more Playwright pass over two screens in four modes; not measured separately.
