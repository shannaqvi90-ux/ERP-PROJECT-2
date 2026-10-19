# p03-identity: letting one new device in from a paused shared address

Date: 2026-10-19 (round 13, wave serial-9)

## The gap (critic p03 round 12)

In the shipped deployment every browser reaches the app from the compose gateway. Round 12 let a
valid set-up code and a known device (one that signed in before) past a pause on such an address.
Anyone else was still kept out. An anonymous script sent five wrong passwords for the finance
manager's e-mail. After that, the finance manager's right password from a new laptop, a fresh
browser or cleared cookies answered 429 for 15 minutes, again and again. The only way back in was
for an administrator to reset the password to a set-up code, which throws the password away and
does not help on the next new device. The paused message also said "from this device" to a device
that had made no failure.

The deployment half (a reverse proxy that forwards the client address, `ERP_KNOWN_PROXIES` in the
shipped compose) is the owner's decision, needs-human #15. It is not touched here.

## Why the right password alone cannot get past the pause

Behind a shared address the server cannot tell the person from the script. If the right password
got past the pause, so would the script's guesses, and the pause would protect nothing. Only
something the script does not have can let one browser through:

- a device cookie from an earlier sign-in to the account (round 4);
- a set-up code (round 12);
- a passkey (never paused);
- a session the person already has on another device, or an administrator.

This round adds the last one.

## Decision

1. **The paused screen shows a device code.** When a browser that is not a known device of the
   account is paused on a shared address, the 429 `auth.signInPaused` answer also carries
   `sharedAddress: true` and a `deviceCode` such as `K7QM-3XRA`. The browser gets a day-long signed
   `erp_pending` cookie holding a random pending-device id. The code is 8 symbols of the set-up-code
   alphabet: an HMAC, under this deployment's key, of the e-mail address and that id
   (`TrustedDevices.ApprovalCode`). It is the same for that browser and that address, different for
   every other browser, and says nothing about the account. It is made the same way whether or not
   an account has the address, so it reveals no account.

2. **The person or an administrator lets that device in.**
   - `POST /api/identity/me/devices/approve` (`identity.profile.update`): the person, on a device
     where they are signed in. It needs a session signed in within the last few minutes, the same
     rule as adding a passkey, so a browser left signed in cannot let other devices try the
     password. Otherwise it answers 403 `auth.recentSignInRequiredDevice`.
   - `POST /api/identity/users/{id}/devices/approve` (`identity.users.update`): an administrator.
     It has the same target check as Allow sign-in again: only users within the caller's own
     access, never oneself.
   - The approval is kept on the user: `device_approval_hash` (the SHA-256 of the code) and
     `device_approval_until` (60 minutes by default, `Erp:Auth:DeviceApprovalMinutes`, clamped
     5–1440). The audit trail records it like any change to the user. One approval is held at a
     time; a new one replaces the last.

3. **The sign-in function counts that browser as itself, on that account only.** The reviewed
   `identity.verify_sign_in` gains `p_pending` (`device:<pending id>`) and `p_approval` (the hash of
   the code made from it for the e-mail typed). For each account with the e-mail it now works out
   which client the attempt is counted as. On the account whose unexpired approval matches, that is
   the pending device: a client of its own, not the shared address. On every other account,
   including the same e-mail in another workspace, nothing changes. Failures are then counted under
   each account's own client key; until now there was one key per attempt.

4. **The device signs in with its password and becomes a known device.** The first success uses the
   approval up (both columns cleared). It is recorded under the device, and the browser keeps its id
   as a known device of the account (`erp_device`), so later sign-ins from it need nothing more.

5. **The screens.**
   - The sign-in screen says the address is shared by many people, so the failures may not have been
     this person's, and that sign-in is paused until when. It shows the device code (left to right,
     selectable) and says what to do: give the code to the administrator, or enter it under My
     account on a signed-in device; or wait, ask for sign-in to be allowed again, or use a set-up
     code. It no longer says "from this device" on a shared address.
   - My account has "Let a device sign in".
   - The user's Sign-in history shows the same form whenever a shared address is paused or an
     approval is waiting, and says until when a device that was let in may sign in.
   - The palette has "Let a device sign in…" (it focuses the code field). Enter submits.
   - Every string is in English and Arabic.

## Why brute-force protection is no weaker

- A device that is let in still needs the password, and its own failures pause it after the same
  threshold as any client (the gate test fails a let-in device five times, then refuses its right
  password).
- Everyone else behind the address stays paused: the script, other fresh browsers, and a browser
  holding its own code that nobody approved.
- Each approval gives exactly one browser one client's budget on one account. It comes from a
  person with a recent sign-in to that account, or from an administrator whose access covers the
  account.
- The code is worthless to anyone else. It only works with the pending-device id it was made from,
  and that id is in a cookie signed with the deployment's key. Finding another id with the same
  code takes about 10^12 tries, and each id comes from a request to the server.
- `device_approval_hash` is readable by the application role. It is reviewed in
  `tests/Gates/app-readable-credentials.txt`: knowing the code signs nobody in.

## Gates

- `G1SignInThrottleTests.A_device_let_in_signs_in_with_its_password_from_a_paused_shared_address_and_nothing_else_does`
  covers:
  - the code shown on a shared address, stable per browser and different between browsers, and
    never shown on an address of one client;
  - a malformed code refused (400), and the administrators' endpoint refused on oneself (403);
  - the script, a fresh browser and an unapproved browser still paused;
  - the same e-mail in workspace B still paused for the approved laptop, recorded as `throttled`
    there;
  - the laptop signing in, the approval used up, the laptop known afterwards, and the gateway
    still paused;
  - a let-in device pausing itself after its own failures;
  - self-approval refused for an old session (`auth.recentSignInRequiredDevice`) and accepted after
    a fresh sign-in.
- The gate lists were reviewed for the new endpoints and function: the endpoint-permission map,
  the write sets, the security-definer allowlist signature, and the tenant-setting uses in the new
  migration.
- The G2 body builders now take a documented pattern's example for a text field whose made-up
  value breaks the pattern. The field-variant gate varies such a field within its pattern (another
  example, or the other letter case) instead of reporting a blind spot. Both approve endpoints are
  now attacked by the takeover gates with valid codes. Before, they would have answered 400 and
  told the gate nothing.
- Web: `SignInPage.test.tsx` covers the shared pause in English and Arabic, with the code shown.
  `screens.test.tsx` covers My account (checked before sending, sent as typed, refusal shown) and
  the sign-in history form in English and Arabic.
- e2e (`sign-in.spec.ts`): paused on the shared gateway, a new browser shows its code; the
  administrator types it in the person's Sign-in history; the browser signs in with its password;
  the gateway stays paused for everyone else.

## Alternatives not taken

- **Let a right password past a shared pause.** Rejected: behind a shared address it is the
  script's password guess too.
- **A proof of work for fresh browsers.** Rejected: a script with a GPU outpaces a browser by orders
  of magnitude. It keeps nobody out and slows only the person.
- **E-mailing a one-time code.** Rejected: it needs a mail service and its credentials (a human
  gate), and the deployment has none.
- **Keeping approvals in their own table.** Rejected: two columns on the user carry one approval per
  account with the user's existing row-level security and audit trigger, and the sign-in function
  already reads that row.

## Processor time this round adds

The tests added this round, measured on the builder's runs:
- the gate test, about 8 s wall in the throttle fixture;
- the rate-limit test, about 1 s;
- the takeover and write-set gates exercising the two new endpoints, a few seconds;
- one e2e test, about 15 s;
- four web unit tests, under 1 s.

That is roughly 60 to 100 processor seconds in all, against the 10,500 s maximum.

## This round's verify run (through the verify slots, ports 20310 and 20311)

- The slot was taken at 2026-10-19T05:30:06Z. The run passed in 3,666 s, and the ratchet check
  passed. Load reached 69.7 on 16 CPUs early in the run and fell to 2.3 by its end. Wall time was
  therefore not judged.
- Processor time was 9,983 s against the 10,500 s maximum: .NET 7,648, web 1,994, e2e 306,
  timing 35.
- Every test passed: .NET 732, web unit 528, e2e 113, compare 415, build 6. The suite minimums are
  raised to those counts.
