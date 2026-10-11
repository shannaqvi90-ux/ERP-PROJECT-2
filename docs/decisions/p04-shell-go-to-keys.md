# p04-shell: go-to keys, record commands from anywhere, and the help sheet's list keys

Round 9.

## Go-to keys (G, then a letter)

Reaching a named screen by keyboard won against Odoo by only 0.05 s of modelled human time
(Alt+M, Enter: two units of action, each with a mental step). Experts in keyboard-first tools move
with two-key sequences (G then a letter). The shell now offers them:

- `G`, then `H` opens Home; modules declare letters for their own navigation entries in
  `web/src/modules/<name>/extensions.ts` (`goTo: [{ key, menu, code }]`): identity `U` Users,
  `R` Roles, `M` My account; tenancy `C` Companies, `B` Branches, `A` Company access, `W` Workspace.
  `collectExtensions` refuses a non-letter, a duplicate, the shell's own `G` and `H`, and an entry
  of another module.
- Keys are positions (`KeyboardEvent.code`), like every other shortcut, so an Arabic layout works
  the same.
- Only when no field has the focus and no dialog is open (in a field, G is a letter typed). A list
  focuses its search box on arrival, so from a list the keys work after Down moves the focus
  to the rows; from Home and from records they work at once.
- Both keys are taken in the capture phase before the screen's own keys and the shortcut registry,
  so a screen's plain key (n, /) never runs as the second key of a go-to.
- G shows what it offers for two seconds (a hint with each letter and screen, `role=status`); Esc,
  any other key or the time running out ends the wait.
- Offered only for entries the user's navigation lists (permissions decide the menu on the server).
- The navigation pane draws each entry's letter from `data-go` (CSS), keeping the entry's text and
  accessible name as they were, and sets `aria-keyshortcuts="G U"`; the help sheet has a "Go to a
  screen" group.

Alternatives: per-screen chords (Alt+Shift+letter) collide with browsers and assistive tools and
need three keys; Alt+digit switches tabs in Chrome on Linux; letters derived from labels would
change with a user's permissions and language. Declared letters are stable.

The comparison driver for `reach-screen-keyboard` gains a `goTo` variant (G, then U); the harness
counts the best path per metric, and the navigation-pane and palette paths stay.

## Record commands from anywhere

The round 8 critic found that "copy role", "unblock" and "sign-in history" found nothing in the
palette from Home. Module actions may now ask for a record first (`pick`, a string key saying what
to choose): the action opens its list, the status line asks for the record ("Choose the role to
copy, then press Enter."), and the command runs once the record the user opens offers it, under
the record's own conditions (Copy only for a role the user may copy, as its button). An action may
name a second command (`then`) run after the first: Allow a user to sign in again opens the
history tab, then runs Allow sign-in again if the chosen user is paused, or says it is not
available. An action that asks for a record waits as long as its screen is shown and is dropped
quietly when the user leaves it. `requires` adds permissions an action needs besides its own
(Allow sign-in again needs `identity.users.update` and `identity.signIns.read`).

The reader sweep in `paletteActions.test.tsx` runs these actions too, choosing the first row as a
user would.

## Help sheet and users with no screens

- The help sheet lists the list grid's own keys (arrows, PgUp/PgDn, Home/End, Enter, Space, Ctrl+A,
  /, Down from the search, Esc) in an "In lists" group, in English and Arabic.
- A user whose roles open no screen has no navigation pane; its commands (Alt+M, Alt+B), their
  palette entries and the menu button are no longer offered.

## Processor time

Web unit tests only (about 25 new tests, a few seconds); no server or gate work.
