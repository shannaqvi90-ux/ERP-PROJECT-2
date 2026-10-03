# p02 — Working company and branch; the top-bar switcher as a shell slot

Date: 2026-10-03. Piece: p02-tenancy. Status: accepted.

## Decision

- The company and branch a user works in is stored **per user** (`tenancy.user_workplaces`, one
  row per user). It survives sign-out and applies to every tab. If the user loses access to that
  company or branch, or it is deactivated, the next request falls back to the first active
  company (by code) and its first branch the user may work in.
- `GET /api/tenancy/workplace` returns the current choice and every active company and allowed
  branch. `PUT /api/tenancy/workplace` switches. Permissions are `tenancy.workplace.read` (the
  read-only role has it) and `tenancy.workplace.switch`. A user without the switch permission
  sees the label only.
- The switcher reaches the top bar through a small web extension point. `web/src/kernel/slots.tsx`
  collects `topBarItems` from every `src/modules/*/shell.tsx`, and the shell renders
  `<TopBarSlot />` once. The tenancy module adds `WorkplaceSwitcher` (Alt+C opens it; typing
  filters company and branch codes and names in both languages; arrows, Enter and Escape work).
  After a switch it raises `erp:workplace-changed` on `window` so screens can reload.

## Why

- Per-user rather than per-browser: dynamics-style ERPs keep a "current company" per user. It
  keeps a printed or exported document consistent with what the user last chose. A future
  per-tab override can be added through the same binder.
- A slot instead of editing the shell: the shell (p04) is being rebuilt in parallel. One
  `<TopBarSlot />` line is the whole contract.
