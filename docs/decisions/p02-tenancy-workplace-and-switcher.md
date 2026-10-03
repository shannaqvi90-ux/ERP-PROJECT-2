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
- The switcher reaches the top bar through the shell's extension points (p04,
  `web/src/kernel/extensions.ts`). `web/src/modules/tenancy/extensions.tsx` contributes it as a
  `topbar` item (permission `tenancy.workplace.read`). It also adds a `palette` source: "work in
  company · branch" for every company and branch the user may switch to (permission
  `tenancy.workplace.switch`). The switcher registers Alt+C with the shell's shortcut registry, so
  it is listed in the shortcut help sheet. Alt+C opens it; typing filters company and branch
  codes and names in both languages; arrows, Enter and Escape work. After a switch it raises
  `erp:workplace-changed` on `window` so screens, and the switcher after a palette switch,
  reload.
- When the user may work in at most six active companies, every other company also gets its own
  button beside the switcher. Switching is then one click, and the branch defaults to the first
  the user may work in. Most UAE SMEs run between one and a handful of legal entities, so this
  is the common case. The list (Alt+C) remains for branches and for larger groups.

## Why

- Per-user rather than per-browser: dynamics-style ERPs keep a "current company" per user. It
  keeps a printed or exported document consistent with what the user last chose. A future
  per-tab override can be added through the same binder.
- An extension point instead of editing the shell: the shell (p04) owns its layout. The tenancy
  module adds to it from its own folder.
