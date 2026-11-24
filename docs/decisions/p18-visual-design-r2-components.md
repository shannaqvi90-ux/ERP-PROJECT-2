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

## Overflow titles and the comparison harness's health check (integration fix, 2026-11-22)

The integrator's three alone verifies failed only in the comparison harness's health check. Three causes were found and fixed:

1. The sweep measured a cut placeholder with a hidden span and re-armed itself through its own observer (fix 3ad9cf14, later replaced by the canvas measure 789d20f1; `takeRecords()` after every sweep stays).
2. `parseFloat` in `overflowTitles.ts` was refused by the money gate (9b8eed63).
3. The sweep ran from a 120 ms timer armed by the observer, so its title changes were page changes in a task the harness could not tie to a step. The sweep now runs in the observer's own callback (a microtask of the task that changed the page), only around the changed nodes; a change of density, language, direction or theme sweeps the whole page at once; only a window resize uses a timer. No sweep is armed at page load.

Measured on a clean stack (2,000 users) after the 180 Playwright specs: all 24 built ours tasks verified, with and without the e2e state. Under artificial load (22 busy loops, load about 23) the health check is itself intermittent: `import-5000` was invalid in 2 of 12 runs with the sweep removed entirely and in 2 of 12 runs with this version (and `edit-and-save` and `attach-file` in other runs, with "a DevTools command outside any step"), so that class of failure is the harness under load (gate backlog row 8's class), not this piece's sweep. A health check that fails only on such a task on a busy machine should be re-run when quiet. Full e2e on a fresh stack: 180 passed (11.0 min). Web unit tests: 806 passed. Gate classes Money, Shell, String, ModuleBoundary, Registration and G1ClientState: 23 passed. Processor time added: none measurable (no new test projects; one unit test).

## Round 3: overflow titles come from the render, not from observers or pointer moves

Decision: `kernel/overflowTitles.ts` sets a cut-off element's `title` (a) in `useOverflowTitles()`, a
layout effect called in AppShell, ListView, RecordForm, SearchSelect and CommandPalette, and (b) on
`focusin`. No pointer handler, timer or observer. Reason: Chromium aligns pointermove/pointerover
with the animation frame, so a DOM write in a pointer handler is a task the comparison harness cannot
trace to a step (import-5000 and export-filtered-list went invalid). The hook first ran as a passive
effect; one of two fresh-stack full health runs then made import-5000 invalid on a style recalc
0.2 s after step 2, and another made custom-field-filter invalid, because a passive effect can run
in a later scheduler task of no traceable origin. A layout effect runs inside the commit of the
render that shows the text.
Evidence: on fresh stacks built like verify's (2,000 users), the full ours health run
(`run.mjs --task built --product ours --health`) verified all 24 tasks (import-5000 included) in
two runs; web unit 808 of 808; Playwright design, shell, keyboard-cycle, lists, navigation specs
all passed; source and allowlist gate classes passed (30 of 30). Not a full `./erp verify`.
