# p04 — The command palette runs module actions and the screen's own commands

Date: 2026-10-10. Piece: p04-shell, round 8. Status: accepted.

## Problem

The round-7 critic's biggest gap: the palette promised "open any screen, record or action by
typing", but the only actions it knew were the shell's own (language, digits, preferences,
shortcuts, navigation pane, print, sign out). Typing "new user", "new company", "export" or
"copy role" found nothing, on any screen, although each screen had the action on a button or a
key. The cause was the extension contract (`web/src/kernel/extensions.ts`): a module could give
the palette record sources only. Every later module (invoices, purchase orders, journal entries)
would have inherited a palette that finds a record but cannot start one.

## Decision

1. **Screen commands in the shortcut registry.** The registry that holds the global shortcuts
   (`kernel/shortcuts.tsx`) now holds a screen's commands too: `useCommand({ id, labelKey,
   keywordsKey?, chord?, enabled, run })` registers what the screen offers while it offers it;
   `useShortcut` is the same with a key. The palette lists every active command of the screen on
   show first, under "On this screen" (the shell's own `shell.*` ids excepted: they are its
   Actions). A screen registers a command with the same condition that shows its button
   (`enabled`), so the palette offers exactly what the screen offers, and runs exactly what the
   button runs. Chordless commands never match a key and are not on the shortcut sheet.
   Registered now: the list's Search, Columns, Views and Print or export (every list screen);
   each record's tabs ("Open the Sign-in history tab", every form with tabs); a role's Copy and
   Delete; a user's Reset password, Sign out everywhere, Delete and Allow sign-in again; New on
   the users, roles, companies and branches screens (now labelled "New company", "New branch"
   instead of "New record").
2. **Module actions through the extension contract.** `ModuleExtensions.actions` lets a module
   offer an action from anywhere: `{ key, labelKey, keywordsKey?, permission, path, command?,
   available? }`. Starting it opens its screen (`path`) and then runs that screen's own command
   (`command`) once the screen offers it, so there is one implementation of each action, the
   screen's. Identity offers New user, New role and Print or export of either list; tenancy New
   company, New branch and Print or export of either list.
3. **Permission, checked twice, never offered when it cannot run.** The shell offers an action
   only to a user whose roles grant its `permission` and the permission of the screen it opens
   (`offeredActions`, the same `can()` the navigation uses). The screen is the second check: it
   registers the command only when its own test allows the action. When a screen's test depends
   on data rather than a permission (New branch needs a company the user may add a branch to),
   the action declares `available()`, which runs the screen's own predicate (`branchCompanies`,
   exported from the branches screen) each time the palette opens; the action is not shown until
   it answers yes. If a screen still does not offer the command within five seconds (the user
   stayed on a form with unsaved changes), nothing runs and the status line says so.
4. **Words in both languages.** Every action and command matches its name in both languages and
   optional keywords in both (`…Keywords` strings: "invite", "export", "دعوة", "تصدير"), so
   "new user" finds مستخدم جديد on an Arabic screen and "invite" finds New user.
5. **Ordering.** With a query, the group whose best entry matches best comes first, ties in the
   fixed order (On this screen, Screens, Actions, then record sources): "new" on the users screen
   gives New user first, "us" from home still gives the Users screen first (the comparison
   harness's palette path is unchanged).

## Gates (written before the actions, then made to pass)

- `web/src/modules/shell/paletteActions.test.tsx` (G2 in the palette): every action is declared
  whole (permission, its module's screen, English and Arabic name and words); for every action,
  the palette offers it with all permissions and not without its permission or its screen's;
  started from home, every action opens its screen and runs the screen's command (a new record,
  or the print menu); a user's account commands are offered exactly when the record shows their
  buttons; New branch is not offered to a user with no company to add one to; and the sweep: a
  user who may only read runs every entry the palette offers on home, the users, roles, companies
  and branches records and the sign-in history tab, accepting whatever each opens, and no write
  leaves (own settings and views excepted), no new record opens, and no module action is left
  unrun by its screen. Its control runs the same sweep for an editor and must find the copy,
  delete, reset, sign-out-everywhere and unblock writes.
- `web/scripts/palette-plant-self-test.mjs` (run by `./erp verify`): ten plants, each must be
  caught by an assertion: an action offered without its permission (PA1), without its screen's
  (PA2), the shell passing actions unfiltered (PA3), New user declared under the read permission
  (PA4), `enabled` ignored by the registry (PA5), Copy role, Reset, Delete user and Allow sign-in
  again registered without their checks (PA6 to PA8), New branch's availability test removed
  (PA9).
- Ratchet: `g2.paletteActionsChecked` 8, `g2.paletteEntriesSwept` 41 (the gate reads them from
  `gauntlet/ratchet.json`), `suite.webUnitTests` 425, `suite.e2eTests` 93.
- End to end (`tests/e2e/specs/shell.spec.ts`): Ctrl+K "new user" Enter from home opens a new
  user with the e-mail focused; "invite" finds it; "export" on the users screen gives the list's
  Print or export first and opens its menu; "new company" from home opens a new company; the
  read-only demo user is offered no "New …" action and is offered Print or export of the users
  list.

## A New that runs before its list has loaded

A module action runs its screen's command as soon as the screen offers it, which on a busy server
is before the list's definition has arrived. The list then opened what the address said (the bare
screen) and dropped the new record. New (`useRecordPanel().startNew`, every list screen) now writes
`/screen/new` into the current history entry at once (`replaceAddress` in the router, the one
reviewed history writer besides `navigate`; `tests/Gates/client-carriers.txt` updated), so the
list opens the new record whenever it arrives. A vitest case with a slow list holds it.

## Also in this round (critic p04 round 7, minor findings)

- At 390 px in Arabic a wrapped toolbar button ("مستخدم جديد") grows to hold its two lines
  instead of spilling out (`kernel/lists/lists.css`, phone widths only).
- The navigation pane's open or closed state is remembered for wide windows only. A window that
  turns narrow puts the pane away; one that turns wide again gets it back as it was left there
  (before, the pane closed by itself on a phone-width screen change and stayed closed when the
  window was widened again).

## Processor time this round adds to `./erp verify`

Measured on this machine under other agents' load: the new vitest file about 30 s; the palette
plant self-test about 40 s of processor time (87 s wall: a control run of the gate and ten
planted runs of only the tests each plant concerns, each stopping at its first failure); three
end-to-end tests about 15 s. About 85 s in all, well inside the room under `verify.cpuSeconds`.

## Rejected

- Harvesting buttons from the screen's DOM for the palette: no permission semantics, labels of
  icon buttons are missing, and a later module could not say what an action is called.
- Module actions that call the API themselves: a second implementation of each action beside the
  screen's, with its own permission check to keep in step. Running the screen's command keeps one.
- Showing an action whose screen may refuse it and saying so afterwards: the lead's rule is that
  an action a user may not run is not offered; `available()` exists for that case.
