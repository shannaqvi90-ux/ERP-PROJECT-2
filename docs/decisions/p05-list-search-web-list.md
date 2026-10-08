# p05 — The list screen: one virtualised, keyboard-first component for every list

Date: 2026-10-03. Piece: p05-list-search. Status: accepted.

## Decision

`web/src/kernel/lists/ListView.tsx` renders any registered list from its definition
(`/api/lists/{key}/definition`): toolbar (title, search, count, views, columns), filter chips, a
selection bar, the grid, a details panel and a key hint line. Strings live in
`web/src/modules/lists/i18n/{en,ar}.json`. A screen is a few lines (`UsersPage.tsx`): list key, title,
count message, search placeholder and optional cell renderers, open handler and bulk actions.

- **Grid.** A `<table role="grid">` laid out with CSS grid rows; only the rows in view (plus ten either
  side) exist, absolutely positioned in a body as tall as every row (28 px rows; 100,004 rows scroll
  like 20). Rows load in chunks of 100 by keyset (the previous chunk's `next`) and by offset when the
  user jumps; the previous rows stay on screen until a new query's first page arrives.
- **Keyboard.** The search box has focus when the list opens; typing searches after 150 ms; Enter
  opens the only match or moves into the grid; ↓ moves into the grid. In the grid: ↑ ↓ Page Up/Down
  Home End move (`aria-activedescendant`), Shift extends the selection, Space selects, Ctrl+A selects
  the loaded rows, Enter opens, `/` returns to search, Escape closes the details panel, then clears the
  selection, then returns to search.
- **Headers.** Click sorts (again: reverses; Shift+click adds a key); the ▾ menu sorts, opens a filter
  editor suited to the column type (text operators, choice check boxes, yes/no, number and date
  ranges in the user's time zone), groups and hides. Conditions show as removable chips.
- **Views.** Standard, built-in, shared and personal views; save as new (share only when allowed,
  optionally as default), save changes, delete. The user's default (else a shared default) opens
  with the list.
- **Address.** Search, filter, sort, grouping, columns, view and the open record live in the query
  string, so a reload or a shared link opens the same list.

No new dependency: virtualisation, popovers and the filter text are a few hundred lines of our own,
cheaper to keep right-to-left correct and keyboard-complete than to adapt a library. TanStack Virtual
(MIT) would cover only the virtualisation; full grids such as AG Grid Community (MIT) bring their own
keyboard, selection and right-to-left models and several hundred kilobytes, and the features that
matter here (server-side row model, grouping) are in their commercial editions.

## Why

The bar compares "find one record among 100,000" with Odoo: Odoo needs the Apps menu, the Contacts
app, the full name, Enter and a click on the result (5 steps, 30 keys, 3 clicks, 1.04 s). Here: the
list link, the words (lower case, any order, any part), Enter (3 steps, no extra click; about 0.2–0.35 s
on this machine with 100,004 users). Every other list module (Contacts in p16, documents later) plugs
into the same component and the same server contract.

## Screens with their own record panel (integration with p03)

The users screen keeps p03's user panel (details, access, sign-in history, account actions) and
p03's new-user form; the list framework supplies the grid, the search and the address. Three small
`ListView` options make that possible without a second list implementation:

- `renderRecord(id, close)`: the screen's own content in the list's "Details" region for the open
  record. The list still owns `?open=id`, so a link or a reload reopens the same user.
- `openId` / `onOpenIdChange`: the screen can open a record itself (the user it just created, with
  the one-time set-up code shown once) and hears every open and close.
- `openOnClick` and `reloadKey`: a single click opens a row on screens with a side panel, and the
  screen asks for fresh rows after a save.

The list keeps any address parameter it does not own (`?new` for the new-user form). `?search=` is
read as `?q=` so older links keep working; the command palette now links with `?q=`. Cell values of
type date and money go through the kernel formatter (`useI18n().format`), so they follow the user's
digit choice and money is formatted from its decimal string, never a binary float.

## Round 2 additions (2026-10-03)

- **Keys on header buttons.** The grid handles keys only when it has focus itself; Enter and Space
  on a column header's sort or menu button press that button (round 1 opened the active record).
  A click on a row's check box keeps the keyboard in the grid.
- **One column layout.** The header and every row share one grid template and one minimum width
  (the sum of the columns' minimums), so a long e-mail is cut with an ellipsis instead of widening
  its row's columns (round 1's rows shifted by up to 37 px).
- **Best match first.** While the user searches without having chosen a sort, the list asks for
  none and the server ranks (see `p05-list-search-relevance.md`); when the page says `ranked`, the
  top row is marked and Enter in the search box opens it. The section is `aria-busy` until the rows
  shown answer what was typed.
- **Any column's filter.** With header filters on two or more columns, a toggle switches between
  rows matching every filter and rows matching any column's filter (`a or (b1 and b2)`: a range on
  one column stays together). Views and addresses read both forms back as editable conditions.
- **All that match.** Ctrl+A selects the loaded rows; Ctrl+A again (or the selection bar's link)
  selects every row that matches. Copy then fetches them page by page, at most 5,000 (more is an
  export, p14); a bulk action acts on all of them only if it declares `runAll(query, total)`, and is
  disabled otherwise.
- **Command palette.** Picking a user opens that user's record (`?q=…&open=id`), not just the
  filtered list.
