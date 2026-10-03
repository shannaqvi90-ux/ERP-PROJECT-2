# p03 — Users and roles screens

Date: 2026-10-03. Piece: p03-identity. Status: accepted.

- **One screen per list, with a side panel** for the selected record (`?user=<id>`, `?new`), so
  the list keeps its place and search while a user is created or edited. p05's list framework can
  replace the table later without changing the panel.
- **Keyboard first**: `/` search, `n` new, arrows move through rows, Enter opens, Escape closes,
  Ctrl+Enter (or Ctrl+S) saves in every form. Creating a user: type only the part of the e-mail
  before `@` (the signed-in administrator's domain is shown and added on Tab), the display name is
  suggested from it, the role filter's Enter ticks the first role shown, Ctrl+Enter creates and the
  set-up code appears once with a copy button.
- **Permission matrix**: a block per module, a row per resource, columns for view, create,
  change, delete and an "other" cell; search narrows rows; module, row, column and "all shown"
  bulk toggles. A permission the signed-in user does not hold is shown disabled (the server
  refuses it anyway). Resource labels come from the server (`resource.<module>.<resource>`,
  falling back to the permission's own label), so other modules need no web strings for it.
- **What they can do and why**: the user panel's access tab lists each permission with the roles
  that grant it; the sign-in history tab shows attempts and paused clients with Unblock.
- Screens hide what the user's roles do not grant (buttons, fields, tabs); the API enforces it.
- Styles live in `web/src/modules/identity/identity.css` (logical properties only, so Arabic
  mirrors); two small shared classes (`.notice`, `.signin-hint`) were added to `styles.css`.
