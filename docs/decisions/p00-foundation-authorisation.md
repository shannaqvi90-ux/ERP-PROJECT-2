# p00 — Authorisation: one permission per endpoint, deny by default

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Decision

- Permission keys are `module.resource.action` (lower camel case parts). A module declares its
  catalogue with `module.Permissions(...)`; labels live in its `Resources/{en,ar}.json` under
  `permission.<key>`.
- Every endpoint calls `.RequirePermission(key)` (exactly one) or
  `.AllowAnonymousReviewed(reason)`. The **fallback policy denies** (an unsatisfiable requirement),
  so anything forgotten is closed.
- **The host refuses to start** if an endpoint declares no permission, more than one, both, or a
  permission missing from the catalogue (`ErpPlatform.ValidateEndpoints`).
- Anonymous endpoints must also appear in `tests/Gates/anonymous-allowlist.txt` with a reason; the
  G2 gate fails on a missing or stale entry, and `gauntlet/ratchet.json` caps their number.
- Roles are tenant data (`identity.roles.permissions text[]`). A user's permissions are the union
  of their roles' grants, intersected with the live catalogue, and are re-read on every request
  (a role change applies at once).
- **No privilege escalation**: a caller can only create, change, assign, unassign or delete roles
  whose added or removed permissions the caller holds; nobody changes their own roles or deactivates
  themselves; the Administrator system role is maintained by the platform (re-synced to the full
  catalogue by the seeder) and cannot be edited or deleted.
- The session response lists the user's permissions and the menu filtered by them; the web app
  hides screens and actions the user cannot use (`can()`, `<Can>`), and the API enforces them.

## Why

- The owner's G2 gate: "every action is denied unless a role grants it", and granting exactly one
  permission must open exactly that action. One permission per endpoint makes that testable
  mechanically from the running app's endpoint list.
