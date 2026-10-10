# p03 identity: an administrator stops a lost or stolen device (round 9)

## Context

Round 8 added passkeys. The critic's biggest gap: when an employee's laptop is lost, the
administrator uses the user panel's "Reset password…" and "Sign out everywhere", and the passkey
on the laptop still signs in straight afterwards. No screen showed a user's passkeys, the sign-in
history did not say whether a sign-in used one, and the only lever that worked was deactivating
the account, which also locks out the person.

## Decisions

1. **The user panel shows the user's passkeys** (name, added, last used, synced) under the
   account actions, to anyone who may read users. Whoever may act on the account (identity.users.update,
   and the account within their own access, the same rule as every other account action) removes
   one passkey (the lost laptop, keeping the phone) or all of them, each after a question whose
   confirm button has the focus (Enter confirms, Escape keeps them). The user's own passkeys are
   still managed on My account.

2. **One passkey alone is `DELETE /api/identity/users/{id}/passkeys?passkeyId=…`**, not a second
   route parameter. A passkey id that is not that user's answers 404 (another user's, another
   workspace's and a random id alike). The query form keeps one route per action, so the G2
   takeover gate (which aims every user-addressed write at stronger users and needs the same request
   to succeed on a user without roles) covers it without a gate change.

3. **Sign out everywhere takes `removePasskeys=true`** (query, as the endpoint has no body). On
   screen, when the user holds passkeys, the button reads "Sign out everywhere…" and asks first,
   with "Also remove their passkeys" ticked: ending sessions is the lever for a lost device, and a
   device that can sign straight back in has not been stopped. Without passkeys it acts at once,
   as before. The API default stays false (sessions only), so existing callers keep their meaning.

4. **Reset password takes `removePasskeys`** in its body, and the screen offers it as a tick box,
   **not ticked**: most resets are for a forgotten password, and that person still has their device.
   The done message and the set-up code notice say how many passkeys were removed.

5. **Permissions.** Removing passkeys through Sign out everywhere or the stand-alone DELETE needs
   identity.users.update, as before. Through Reset password it needs identity.users.resetPassword:
   that permission resets the account's sign-in, password and, when asked, passkeys. A helpdesk
   role holding only reset-password can therefore stop a lost device in the same step as the
   reset, but cannot remove passkeys on their own (403). Every path refuses accounts beyond the
   caller's own access and the caller's own account (`TargetProblemAsync`, the one mechanism).

6. **The sign-in history records the method** (`identity.sign_in_attempts.method`: `password` or
   `passkey`). The reviewed SECURITY DEFINER sign-in function is not changed: it records only
   password attempts, so the column's default is `password` and the app writes `passkey` for a
   passkey sign-in. The default is set after the column is added, so attempts recorded before stay
   without a method (passkeys signed in from round 8 on; those rows cannot be told apart) and the
   screen shows none for them. A refused passkey answer is still not recorded against any account:
   the user handle in it is unverified, so writing a failure from it would let anyone write into
   another account's history.

7. **Audit.** Removals are tracked deletes, so the audit trail keeps each passkey removed, with
   its owner and the administrator as the actor (tested).

## Gates (written first)

- G2 takeover (`G2AccountTakeoverTests`): the Administrator and every target user now hold a
  passkey (written straight into the database: invited targets cannot sign in to add one), and the
  user as the administrator reads them includes their passkeys. A request that is refused but still
  takes a stronger user's passkeys now fails the gate. Checked with a plant (reset removes the
  passkeys and commits before the target check): the gate reported "the Administrator's passkeys
  changed" besides the status findings; the plant was removed.
- Identity module test: the lost-device scenario through the API with a software authenticator
  (sessions-only sign-out leaves the passkey working; one passkey removed alone; another user's
  passkey id answers 404 and stays; sign-out with removal and reset with removal refuse the device;
  reset-password alone cannot use the stand-alone removal; history methods; audit actors).
- Screen tests: listing, removing one and all, the Sign out everywhere question with the box
  ticked and its focus, the reset tick box, nothing to remove for a reader without update, the
  history method in English and Arabic.
- End-to-end: a field laptop (Chromium virtual authenticator) adds a passkey and signs in with it;
  the administrator sees it and the passkey sign-in on the panel, signs the user out everywhere
  with the passkeys by keyboard, and the laptop's passkey is refused.

## Processor time

The added .NET test takes about 1 s; the takeover gate gains one GET per user read and one insert
per target (its run stayed at about 36 s for the main test); the screen tests about 0.4 s; the
end-to-end test about 15 s of browser time. No new environment or stack.

Verify runs of this round (through the verify slots, ports 20310/20311):
- Run 1: every .NET, web and comparison test passed; one end-to-end test failed. The palette test
  counted every `table tbody tr` on the page to prove the users list narrowed to one row, and the
  viewer's record panel now also lists the passkey an earlier end-to-end test added. The test now
  counts the list's own rows (`table.list-grid tbody tr`), the same claim on the list.
- Run 2: verify passed in 2,765 s; processor time 8,766 s (dotnet 7,202, web 1,319, e2e 209,
  timing 36) against the maximum of 10,500 s. Counts: .NET 551, web unit 399, end-to-end 90,
  comparison 373; the ratchet minimums are raised to them.
