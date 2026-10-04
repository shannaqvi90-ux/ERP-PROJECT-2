# p03 identity: Alt+N starts a new user or role; a plain n still works outside fields

Date: 2026-10-03. Piece: p03-identity. Status: accepted.

## Context

The Users and Roles lists focus their search box on arrival, which is what makes "find one user
among 100,000" three steps. Critic p03 round 2 found the cost: the advertised "n" new-record key was
typed into the search instead, so the compare driver for create-restricted-user failed every run,
and the e2e test hid it by clicking the heading first.

## Decision

- `Alt+N` (physical key, so it works on an Arabic layout too) is registered with the shell's
  shortcut registry on both screens while the user holds the create permission. Modifier shortcuts
  fire inside fields, so it works from the focused search box. It is listed in the shortcut sheet
  under "Users and access" and announced through `aria-keyshortcuts="Alt+N N"`.
- The plain `n` stays for when no field has the focus (Dynamics-style list navigation).
- The search keeps the focus on arrival: find-user stays at its keystroke count.
- Unit tests press both keys with the search focused and without the create permission; the e2e
  test presses Alt+N on arrival without clicking anywhere first; the compare driver's measured
  path is Users > Alt+N > e-mail > role > Ctrl+Enter.

## Not chosen

- Dropping the search focus on arrival: it would cost find-user the keystrokes the critic just
  credited.
- Making a plain `n` open the form from the search when the box is empty: a user searching for
  "Nadia" would get a form instead of results.
