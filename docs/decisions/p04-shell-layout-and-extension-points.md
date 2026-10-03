# p04 — Shell layout and the extension points modules plug into

Date: 2026-10-03. Piece: p04-shell. Status: accepted.

## Decision

- **Layout** (`web/src/modules/shell/AppShell.tsx`), dense and keyboard first, in the Dynamics 365
  manner: a 44 px top bar (navigation toggle, brand, workspace name, *context slot*, command
  palette field, language button, shortcut help, user preferences, sign out); a 208 px module
  navigation pane on the inline-start side, grouped by the menu entries' `group`; breadcrumbs
  (Home › group › screen) above the screen; a one-line status bar (who, workspace, session expiry,
  module status items, the two keys to remember: Ctrl+K and ?).
- **Navigation pane**: entries come from the session's menu, already filtered by permission on the
  server. One Tab stop (roving `tabIndex`), Up/Down/Home/End inside, Alt+M to jump in. Group
  headings are drawn from `data-label` with CSS `::before` and announced through the group's
  `aria-label`, so the pane's text is its entries alone. The pane hides when closed (Alt+B, kept per
  device) or when the user's roles open no screen.
- **Group headings**: a menu group (`MenuEntry.Group`, for example `settings`) is labelled by
  `shell.group.<group>` in the shell's strings. The shell ships headings for the areas this ERP will
  have (settings, contacts, sales, purchasing, inventory, accounting, manufacturing, people,
  reports); a gate fails when a module uses a group without a heading.
- **Extension points** (`web/src/kernel/extensions.ts`): a module exports `extensions` from
  `src/modules/<module>/extensions.ts(x)`; the shell discovers it with `import.meta.glob`, so no
  central file changes.
  - `topbar`: components in the top bar's *context slot*, right after the workspace name. This is
    where p02 puts the active company/branch switcher. A component takes no props, reads the
    session and strings through hooks, and may register a shortcut with `useShortcut` (it then
    appears in the help sheet; a clash with an existing chord throws).
  - `status`: short facts in the status line.
  - `palette`: command palette sources. `minLength >= 1`: records found as the user types (identity
    contributes users); `minLength: 0`: fixed actions shown on an empty query too (p02: "switch to
    company X").
  - Each item may name a `permission`; the shell offers it only to users whose roles grant it.
    Keys must start with the module's name and be unique per slot (checked at start-up and in
    tests).
- **Focus**: a new screen gets the focus unless it already put the focus inside itself (a search
  field), and changing the language never moves the focus.

## Why

- Every later module needs the same frame; extension files keep modules self-contained (the
  modular monolith rule) and let p02 add its switcher without touching the shell.
- Keeping the pane's text equal to its entries keeps existing tests and screen readers simple.

## Rejected

- A top-bar mega-menu (Odoo's app grid): one more click before every screen; the palette and the
  pane reach any screen in one step.
- Context providers for slots (React context registration at run time): ordering and permission
  filtering are simpler with static discovery, and it works before any screen mounts.
