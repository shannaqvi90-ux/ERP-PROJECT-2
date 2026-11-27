# p18: theme and density belong to the user

Status: decided in round 5 (serial-27b). Date: 2026-11-28.

The spec says the look is "chosen per user and remembered". Until round 4 it was kept in two device
keys (`erp.theme`, `erp.density`), so after one person chose Light and signed out, the next person on
that browser got Light, and the same person on another device got the default (r3 critic).

## Choice

- Two nullable columns on `identity.users`: `theme` (system, light, dark, hc) and `density` (compact,
  comfortable), with check constraints; null means "never chosen" (the default: dark, or high contrast
  when the system asks, and compact). The users table is already audited (audit.capture()), so a change
  is recorded with who and when; the audit screens name the fields and the values in both languages
  (`audit.field.theme`, `audit.choice.identity.users.theme.*`).
- `PUT /api/identity/me/look` ({theme?, density?}, at least one), permission `identity.profile.update`
  (the same as the language and digits). A separate endpoint, not two more fields of
  `/me/preferences`: the preferences body has partial-body gates (G1 shell answers) owned by p00, and the
  look is not a language setting. The answer is the session user, as for preferences.
- `GET /api/auth/session` carries `theme` and `density` on the user (null until chosen).
- Client: `applyUserLook(session.user)` at sign-in, once per identity: the user's own choices, or the
  defaults, never the previous person's. `chooseTheme`/`chooseDensity` change the screen at once and
  send an event; `modules/shell/LookSync.tsx` saves it (keepalive) when the user holds the permission.
  No module-level state, no new storage key.
- The device keys remain as the look last shown on this device, for the sign-in screen before anyone is
  known (a device default is fine before sign-in). Sign-in writes them only when the user has a saved
  choice, and removes them otherwise, so nothing of the previous person survives a sign-in without a choice.

## Considered and not done

- Per-user local key: it would hold a user id on the device and be forgotten at sign-out (the forgetter),
  so the choice would not be remembered.
- Two fields on the preferences endpoint: would change the partial-body shapes the shell gates enumerate.

## Cost

One migration (`UserLook`), one endpoint, three small web files; processor time added is the one endpoint's
share of the G1/G2 generators (small, no list columns).
