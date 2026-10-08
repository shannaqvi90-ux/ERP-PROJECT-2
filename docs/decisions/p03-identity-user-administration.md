# p03 — User administration: set-up codes, resets and acting only on weaker accounts

Date: 2026-10-03. Piece: p03-identity. Status: accepted.

## Invitations and resets without e-mail or new anonymous endpoints

- Creating a user without a password issues a **one-time set-up code** (12 characters from an
  unambiguous 31-symbol alphabet, about 59 bits, e.g. `K7QM-3XRA-PZ9D`), stored as an ordinary
  PBKDF2 hash with `must_change` and an expiry (`Erp:Auth:SetupCodeHours`, default 7 days). The
  code is in the creating response only. The same for POST `/users/{id}/password` without a
  password; with one, it is a temporary password that must be changed (unless
  `mustChangePassword: false`). A reset ends the user's sessions.
- The person signs in with the code as the password; sign-in answers 409
  `auth.passwordChangeRequired` until the request also carries `newPassword`. The same
  `newPassword` field is how signed-in users change their own password (proving the current one);
  it ends every other session. The application never compares passwords itself (see the
  credentials record), so password changes go through sign-in instead of a new endpoint.
- No anonymous endpoint was added: the reviewed allowlist is at its ratchet maximum (12).
- **Sending the code by e-mail is not built**: it needs a mail service and credentials (a human
  gate in CLAUDE.md). The administrator copies the sign-in details from the screen.

## Acting on another account needs all of its access

Resetting a password, ending sessions, clearing pauses or editing a user is a way to take that
account over or lock its owner out, so every such endpoint requires that the target's effective
permissions are a subset of the caller's (`identity.userBeyondOwn`, 403), and the reset, unblock
and sign-out-everywhere endpoints refuse the caller's own account (`identity.notOnYourself`).
Copying a role creates one, so it may only copy permissions the caller holds. G2 gate
`G2AccountTakeoverTests` finds every non-GET endpoint under `/api/identity/users/{id}` from the
running app's routing, aims each at the Administrator as a caller holding only that endpoint's
permission, and requires 403 with the Administrator's record, password and session intact, plus
success against a user without roles; ratchet `g2.takeoverEndpointsChecked`.

## Permissions added

`identity.users.resetPassword` (reset other users' passwords) and `identity.signIns.read` (view
sign-in history). Unblock and sign out everywhere use `identity.users.update`; the access view
(`GET /users/{id}/access`: each permission and the roles that grant it) uses
`identity.users.read`.

## Not done in this round

Roles assigned per company and a per-user default company wait for p02's companies (built in
parallel); `user_roles` will gain a nullable company id then. (Wave 1 integrity check, 2026-10-04:
p02's companies and company scope are integrated now; see `p03-identity-roles-per-company.md`.)
