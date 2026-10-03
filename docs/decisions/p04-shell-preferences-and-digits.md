# p04 — Language and digits per user, saved safely across a reload

Date: 2026-10-03. Piece: p04-shell. Status: accepted.

## Decision

- **Digits** are a per-user preference next to the language: `identity.users.numerals`
  (`latn` = 0123, the default and the common choice in the UAE; `arab` = ٠١٢٣), checked by a
  constraint, audited like every column of the users table, returned in the session's `user` and
  changed through the existing `PUT /api/identity/me/preferences` (`{ language?, numerals? }`,
  at least one; permission `identity.profile.update`). The column lives in identity because it is
  part of the user's profile, like the language.
- Digits apply on **Arabic screens**; English screens always use Latin digits. The browser does
  the shaping: every formatter uses the locale `ar-AE-u-ca-gregory-nu-<digits>` (Gregorian
  calendar always).
- **One step to switch language**: the language button (or Alt+L) changes the screen at once,
  without a reload: `<html lang dir>` flips and the logical-property stylesheet mirrors everything.
- **Saving survives a reload** (routed from p00's round-2 critic): the change is first written to
  this device as *pending* for that user (`erp.pendingPreferences`), then sent with
  `fetch(..., { keepalive: true })` so the browser completes it even when the page unloads. When a
  session loads, that user's pending values win over the session's older values and are sent
  again; they are cleared when the server confirms, or when it refuses for good (4xx other than
  408/429), never on a network failure. Another user's pending values are never applied.
- A user without `identity.profile.update` (or signed out) keeps the choice on this device only,
  and the preferences dialog says so.
- **No failed request on a fresh visit** (routed from p00's round-2 critic):
  `GET /api/auth/session` answers 200 `{ authenticated: false }` without a valid session, also
  when the browser still sends a stale, expired or malformed session cookie (tested). It does not
  delete that cookie: deleting it on the probe let the G1 gate's injected-cookie attack wipe the
  attacker's real session and weakened the rest of the attack. An end-to-end test fails on any
  console error or 4xx response during a fresh visit, with and without a stale cookie.

## Why

- A preference that reverts after a reload looks like a bug; a pending copy plus keepalive covers
  both "the request was cut off" and "the server was slow".
- Intl numbering systems are exact and maintained by the browser vendors; hand-mapping digits
  breaks grouping and decimal separators (٬ ٫).

## Rejected

- A separate shell preferences table/module: an extra request on every load and a second home for
  what is one profile.

## Round 2: signing out survives closing the tab

`POST /api/auth/sign-out` is sent with `keepalive`, like the preference save: when a user signs
out and closes or reloads the tab at once (a shared device), the browser still delivers the
request and the server ends the session. The screen changes to the sign-in form only after the
server answered, so the form never claims a sign-out that did not happen. The end-to-end digits test
waits for the sign-in form before it clears storage and navigates (its sign-out was being
cancelled by the navigation, which made it fail 2 times in 4).

## Round 3: two quick changes no longer collide; signing out replaces the document

The user's row is versioned, so two preference requests that reach the server together (Arabic,
then Arabic-Indic digits, a second apart) could see the later one answered 409, which the client took
as a refusal ("the server did not accept the change"; the critic saw the digits test fail once). A
preference change is a partial last-writer-wins update of the user's own row, so the client sends a
change again on 409 (up to four attempts, 40/80/120 ms apart) and keeps it pending, to be sent at the
next session, if it still conflicts. Signing out now forgets the device's stored state and replaces
the document (see `p04-shell-client-isolation.md`); the screen shows "Signing out…" meanwhile.
