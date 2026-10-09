# p04 — Palette record sources for roles, companies and branches; a failed verify keeps its evidence

Date: 2026-10-07. Piece: p04-shell, round 5. Status: accepted.

## Palette: every record a settings screen lists can be opened by typing

The round-4 critic found that the command palette opened users and offered "work in …", but typing
a role name or a company or branch code found nothing to open. The piece's scope is "open any
screen, record or action by typing".

Decision: the identity module's `extensions.ts` adds a `identity.roles` source (English or Arabic
name, through the roles list's own search), and the tenancy module's `extensions.tsx` adds
`tenancy.companies` (code, English or Arabic legal name) and `tenancy.branches` (code, English or
Arabic name). Each source asks the registered list endpoint (`?search=…&take=5`), so it searches
exactly what the list screen searches, under the same permission and tenant filters. The title is
the name in the screen's language; the subtitle is the code (or, for roles, the other language's
name). Choosing a record opens its list narrowed to it with the record open
(`recordPath(screen, id, q=…)`), as users already did; "Show all matches" opens the list narrowed to
the query. Each source names its read permission, so the palette never asks it for a user without
it (tested). The headings reuse the menu's own strings, so no new strings were needed.

## A failed `./erp verify` keeps what it wrote

The round-4 critic's clean-clone verify failed once in the comparison harness's health check
(sign-in, returning variant, a 120 s locator wait) and passed 12 times afterwards. The cause could
not be read: `verify_cleanup` deleted the scratch directory with the failing result.

Decision: when verify exits non-zero, `verify_cleanup` first copies the scratch directory (stage
logs, test results, the health check's result JSON) out of it and prints where. Round 6: the
integration branch carries one copy of this (`.verify-failed/<UTC time>-<pid>/`,
`ERP_VERIFY_KEEP_DIR` overrides; git ignores the folder), and the merge keeps that copy and drops
this piece's `verify-failures/` variant. The harness
runner records, for any run that errors, what the page showed (`error_page`: address, focused field,
whether a navigation was up, busy indicators and every alert or status message), set-up included,
where no screenshot is taken; the health check prints the whole error and that description on the
console too, so the verify log alone shows it. No timeout was raised and no retry added.

## The top bar never widens the page

Found while checking this round: a user who may work in several companies has the working company
and one quick-switch button per other company in the top bar. Nothing in the bar could shrink
above the phone breakpoint, and labels wrapped, so at 1280 px the shell was 1310 px wide (1359 px
in English), the page scrolled sideways and the navigation pane and Sign out were cut.

Decision: the app grid is one column exactly as wide as the window (`minmax(0, 1fr)`); the top bar
has `min-width: 0`; its buttons, links and the product name never wrap; the module context area
shrinks first while the controls at the end keep their size; the quick switches wrap onto a hidden
second line, so only whole codes show (every company stays one Alt+C away). An e2e test checks
1024, 1280 and 1366 px in English and Arabic (no sideways scroll, a one-line bar, every end
control whole and in view); it fails on the previous build.
