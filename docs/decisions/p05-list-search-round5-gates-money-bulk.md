# p05 — Generic statics and every keyset page in G1, crossed view ids in G2, money totals per currency, bulk change of all that match

Date: 2026-10-07. Piece: p05-list-search (round 5). Status: accepted.

## Context

Round 4's critic planted two faults in p05's own code that every gate let through:

- **L6.** A `static readonly Func<…>` in `ListBinding<T>`, closing over a dictionary of totals keyed
  by list, search and filter (no tenant). Continuation pages (`after=`) reused the total the last
  first page counted, so tenant A's page 2 showed tenant B's count. The process-state gate never
  saw it: statics of generic types were skipped (`ContainsGenericParameters`), and delegate fields
  counted as immutable. The list-answer check judged only the first page's total and groups.
- **P4.** Personal-view routes (`PUT/DELETE /api/lists/{key}/views/{id}`) also matched shared views,
  so a reader without `lists.views.share` renamed and deleted the workspace's shared views. G2 never
  sent a shared view's id to a personal route, or the reverse.

The critic also found that money list columns carried no currency (group totals would add AED to
USD), and that "select all that match" could only copy, never change, the matching rows.

## Decisions

1. **Static state of closed generic types is a root.** `GenericStatics` finds every closed
   instantiation of a product generic type that product code names (field, parameter, local and
   return types, generic arguments, base types) and walks its static fields. The reachable-state
   walk also walks the statics of the closed generic type of every product object it reaches, and of
   its generic bases and declaring types. A static field of delegate type is a finding unless it is
   reviewed: a delegate cannot change, but its closure can hold anything, for the life of the
   process, for every tenant. Applies to every module, not just lists.
2. **Every keyset page is judged.** For each query, both tenants walk the same query in lock step
   over at least four small pages (tenant A's page n, then tenant B's page n), and the total and
   groups of every page of both walks must agree with the walked rows. A list with no query
   answered over more than one page is reported blind.
3. **G2 crosses view ids.** For every list, a reader and a sharer send the workspace's shared view
   id to every personal route, and a personal view's id (the administrator's and the sharer's own)
   to every shared route. All must be 404 (403 is accepted only for a reader on a shared write), and
   every view must be unchanged afterwards.
4. **A money column names its currency column** (`ListColumn.CurrencyField`, CLAUDE.md rule 2). The
   definition check refuses a money column without one. Group totals of money columns are given per
   currency (`ListGroup.MoneyTotals`: currency to amount), never added across currencies, and
   printed lists show each amount with its currency. Base-currency totals belong to p08 (rates),
   which can add a base-amount column next to it.
5. **Bulk change of all that match runs on the server, set-based.** `POST
   /api/identity/users/matching/active` takes the list's own search and filter plus the count the
   list showed (`expectedCount`). It matches through the list binding (`ListBinding.Matching`, the
   same filter the list serves, without order or paging). If the count differs, nothing changes and
   the answer is 409 with both numbers, so the user never acts on rows they did not see counted.
   It applies the rules of one user's edit to the whole set: the caller is never deactivated, and
   users holding a role that grants a permission the caller lacks are left alone. One `UPDATE`
   changes the rest. The audit trigger records each changed row with the caller as actor. Row-level
   security and the tenant query filter confine both the match and the update. The screen asks for
   confirmation with the count first. Cancelling keeps the selection (`runAll` may answer `false`).
   Other modules add their own "all that match" actions the same way: the list's query and the
   count it showed go to the server, never a long list of ids.

## Consequences

- Gate self-test bug 52 (a static `Func` memo in a generic type handing continuation pages the last
  first page's total) must stay caught.
- `tests/Gates/endpoint-permissions/identity.txt` lists the new route under
  `identity.users.update`, the same permission as one user's edit.
- The list-answer phase sends more requests (several pages per query and tenant). That is the
  cost of judging every page.

## Addendum: list headers and cells (round 5, after the relaunch)

6. **A header never leaves its column.** The header cell is a flex row: the label shrinks to an
   ellipsis, and the sort mark and column menu keep their size. The full label is the sort
   button's title. A column's minimum width also fits its header label (about 0.56rem a character
   plus 3.25rem for the menu and padding, at most 16rem), so at desktop widths labels are not cut.
   The alternative, wrapping header labels onto two lines, was rejected: it makes the header row
   taller than the 28px body rows and costs a row of data on every screen.
7. **A text value is a box of its own direction** (`.list-text`: an inline block with
   `unicode-bidi: plaintext` and its own ellipsis) inside the cell, so a Latin e-mail in an Arabic
   list, or an Arabic name in an English one, is cut at its own end and keeps the part that names
   the person, while the box stays at the start of the list's direction. The kernel wraps every
   text column value that is a plain string, a module's renderer included. Setting `plaintext` on
   the cell itself was tried first: Chrome then aligns each value by its own direction (Latin
   e-mails to the left of an Arabic column), and a physical `text-align: right` would break the
   logical-directions gate.
8. **The G2 subject-injection control for a "matching rows" action** searches for the caller's own
   e-mail with `expectedCount` 1, so the valid request acts on the caller alone and any change to
   the named victim is the handler honouring a subject field.

## Addendum: the shell is as wide as the window (round 5)

9. **The shell's one grid column is `minmax(0, 1fr)`**, and the top bar's module context may
   shrink to nothing. Before, the column took the top bar's contents' width: a workspace with five
   or six companies (one quick-switch chip each) made every screen 30 to 500 px wider than a
   1024-1440 px window, cutting off the list's last column, its New button and Sign out. The quick
   company chips now take the room that is left: a chip that does not fit wraps onto a line the
   bar's height clips (`overflow: clip`, so focus never scrolls it into view), so a chip shows
   whole or not at all. Every company stays in the switcher's list (Alt+C), which opens inside the
   window. Rejected: hiding the chips below a fixed width (the right width depends on the number
   of companies and the language) and letting the top bar scroll sideways (a scrolling bar hides
   the sign-out and user controls). This is a shared shell change kept to three CSS rules; p02
   and p04 own the switcher and the bar.
