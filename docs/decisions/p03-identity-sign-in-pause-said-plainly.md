# p03 — A paused sign-in says so, and until when, the same for every address

Date: 2026-10-17. Piece: p03-identity (round 10). Status: accepted. Supersedes the "a paused client
cannot tell it is paused" part of `p03-identity-sign-in-throttling.md`; everything else there stands.

## Context

p00's critics in rounds 7, 8, 9 and 10 (routed to p03 by the lead) found that after repeated
failures the right password still got only "Sign-in failed. Check your e-mail and password and try
again.", with no hint that sign-in was paused or for how long. A person who really is the owner
keeps typing the right password, sees "failed" each time, and concludes the password is wrong.

The pause was kept silent on purpose: it was counted per account, so an address no account has
could never be paused, and saying "paused" would have told an attacker the address exists. Saying
it plainly therefore needs the pause itself to happen the same way whether or not an account has
the address.

## Decisions

1. **Failures are counted by address and client, whether or not an account exists.** A new
   platform table `identity.sign_in_failures (key, occurred_at)` holds one row per counted failure:
   `key` is SHA-256 of `sign_in_secret`, the typed address (lower-cased) and the client source, so
   the table names neither the address nor the client and holds no tenant data. Only `erp_auth`
   (owner of the reviewed `identity.verify_sign_in`) can read, insert or delete it; the application
   role has no privilege on it. Rows older than a day (the longest window) are deleted as new ones
   are counted. Reviewed in `tests/Gates/platform-tables.txt` and `tests/Gates/auth-resolver-writes.txt`.

2. **The rule is the old one, applied to that count.** An account is paused for a client when the
   address's failures from that client, since the client's last success on the account and the
   administrator's last unblock, reach `Erp:Auth:LockoutThreshold` within `Erp:Auth:LockoutMinutes`.
   An address no account has: the same count since the window began. A wrong password and any
   password on a deactivated account count alike (otherwise a deactivated account would never
   pause and would stand out from an unknown address); a right but expired set-up code does not.
   The account's own sign-in history still records every attempt as before.

3. **The function answers when the pause lifts.** `verify_sign_in` gains a third column,
   `paused_until`. When nothing matched and every account with the address (or the address
   alone) is paused for the client, it returns one row with only `paused_until`: the moment the
   threshold-th latest counted failure leaves the window, the earliest across the accounts. That
   moment depends only on the failures the client itself made at the address, so it is the same
   for an unknown address, a deactivated account and a working one. The attempt that reaches the
   limit already answers "paused", so the person does not spend one more try learning it.

4. **The answer is 429 `auth.signInPaused`**, with `pausedUntil` (UTC), `retryAfterSeconds` and a
   `Retry-After` header, and a title in the request's language: "Too many failed sign-ins with this
   e-mail address from this device or network. Sign-in with it from here is paused for about N
   minutes. Wait, or ask your administrator to allow sign-in again." (Arabic with full plural
   forms). It names no count and no threshold. 429 because it is "too many attempts, come back
   later", the HTTP meaning of 429; the code tells it apart from the per-minute request limit
   (`request.tooMany`). A failure before the pause is unchanged: 401 `auth.signInFailed`, no digits.

5. **The sign-in screen** shows the pause in its own words, with the time on the device's clock
   (from `retryAfterSeconds`, so a device clock that differs from the server's still shows the
   right moment) and the minutes left, in English and Arabic, re-rendered on a language switch; the
   password is cleared and keeps the keyboard. The "sign-in may pause" hint is not shown beside it.

6. **The administrator's view** (`GET /users/{id}/sign-ins`, `paused`) counts deactivated-account
   attempts too, so it shows the same pause the sign-in applies. The one difference left: failures
   typed at an address before this account had it count for the sign-in but are not in this
   account's history, so the panel can show no pause while sign-in is paused for up to the window.
   Unblock works as before (it moves the account's count start).

## What the gates now prove

- `G1SignInThrottleTests.Failures_and_pauses_answer_alike_whether_or_not_and_wherever_an_account_has_the_address`
  replaces "give one answer that states no policy". For an address no account has, one in tenant B
  only, one in both tenants, a deactivated account and the gate's user without access, each from a
  fresh client, in English and Arabic: every answer, attempt by attempt (status, code, words), is
  identical, the seconds left agree, the right password while paused gets the same answer, and a
  failure before the pause contains no digit. The bar moved from "a pause is indistinguishable from
  a failure" to "nothing in any answer, failure or pause, depends on whether or where an account
  exists"; the property the old assertion protected (no account oracle) is checked over more
  shapes than before. The other two throttle gates keep every assertion, with paused answers now
  expected as 429 instead of 401 (each attempt is checked in turn: failures up to the limit, then
  the pause with a time in the future).
- `AuthTests.Five_failures_pause_that_client_and_say_until_when_until_an_administrator_unblocks`:
  English words, Arabic words, `pausedUntil`/`retryAfterSeconds`/`Retry-After` agree, the right
  password gets the same pause, an unknown address from the same client gets the same answers and
  words, the administrator's panel shows the same `until`, unblock lets the owner in.
- The credential gates are unchanged and pass: the function still returns no hash, no id and
  nothing on a tenant-bound connection; its reviewed signature is the same.

## Trade-offs

- An attacker learns that their own client is paused and until when. They could count their own
  failures anyway; the threshold and window are not secrets worth the owner's confusion. Nothing
  learned depends on whether the address exists.
- One more platform table, written by a reviewed function. It holds hashes and times only.
- Pauses in progress when this migration runs are forgotten (the old count lived in the accounts'
  histories). Acceptable: at worst an attacker gets one fresh window.

## Also in this round

- G2 self-test `The_takeover_check_catches_a_passkey_removed_whoever_holds_it_and_a_body_flag_that_skips_the_check`
  (critic p03 round 9 asked for P5 and body flags as gate self-tests, beside p00's P4 self-test for
  `?removePasskeys=true`): `LostDeviceBypassModule` plants (a) `DELETE /users/{id}/passkeys?passkeyId=X`
  removing X whoever holds it, caught by the takeover check's "the Administrator's records named
  under a user without roles" probe, and (b) `POST /users/{id}/password` with `removePasskeys: false`
  skipping the target check, caught by the one-field variants aimed at the Administrator. The query
  mechanism is p00's `DocumentedQueries`; no second one was added.
- Permission map, reviewed again: a password reset with `removePasskeys` needs only
  `identity.users.resetPassword`, while `DELETE /users/{id}/passkeys` needs `identity.users.update`.
  Kept: whoever may reset a password may already set the account's password to one they know,
  which is more than removing its passkeys, and the reset stays one lever for a lost device.
  Both still need the target within the caller's own access.
- Role boxes on a read-only user record are each disabled, not only their group.

## Processor time

The round adds one gate self-test that hosts a second app over the takeover environment's database
(two endpoints, about 30 s wall on this machine alone) and five more address runs in the throttle
gate (about 10 s). This branch's passing verify (slot taken 2026-10-17T04:18:15Z, through
verify-slot) used 9,289 s of processor time (dotnet 7,314, web 1,696, e2e 243, timing 35) against
the owner's maximum of 10,500 s, and 3,612 s wall while the machine was busy (load up to 81, so
not judged). The last integration and critic verifies used 9,512 to 9,727 s, so what the round adds
is within run-to-run noise. Counts: .NET 613, web unit 478, end-to-end 102, compare 373; the
.NET and web unit minimums in gauntlet/ratchet.json are raised to them.
