# p00: password sign-in on a shared PC, and a passkey request that never blocks it

Date: 2026-10-10 (round 9). Piece: p00-foundation. Status: accepted.

## Context

The round-8 critic judged the sign-in win to rest on the passkey path alone:

1. **Plain address.** From `/` (not the team's address `/?domain=…`), a whole e-mail did not move on
   to the password, so a password sign-in was e-mail, Enter, password, Enter: 4 steps and 57 keys,
   the reference's e-mail, Tab, password, Enter exactly.
2. **Shared PC.** On a device where anyone once used a passkey (`erp.passkeyOffer=1`), the screen
   asked the device as it opened, for up to 300 s, and kept the Sign in button disabled while it
   asked. The page could not withdraw the request, so the next person's e-mail, Enter, password,
   Enter did nothing until they cancelled the system prompt: 5 steps against the reference's 4. For
   a UAE trading company with shared counter and warehouse PCs this is the common case.

## Decisions

1. **A passkey request never blocks the password** (`SignInPage.tsx`, `kernel/passkeys.ts`).
   - The Sign in button is enabled while the device is asked (it was `disabled={busy || asking}`).
   - Every request the screen makes to the device carries an `AbortController` signal. The person
     typing in the e-mail or the password field, or sending the form, withdraws it: the browser
     closes its prompt, nothing waits on it, no message appears and the keyboard stays where the
     person is typing (a cancellation by the person still says "The passkey was not used.").
   - The note shown while asking says so: "Confirm on your device … Or type your e-mail to sign in
     with your password." (English and Arabic).
2. **After a password sign-in the device's next screen does not ask as it opens.** The device
   setting `erp.passkeyOffer` gains a third value: `"2"` = on, but the last sign-in on this device
   used a password. The screen asks as it opens only on `"1"` (the last sign-in here used a
   passkey, or the setting was just switched on); on `"2"` the button "Continue with a passkey"
   asks, and a passkey sign-in makes it `"1"` again. My account shows the setting on for both. On a
   personal laptop of a passkey user nothing changes; at a shared counter PC the next person after
   a password sign-in meets no prompt at all.
3. **The whole address moves on from the plain address on a device that signed in before**
   (`kernel/signInAddress.ts`). After every successful sign-in the device keeps the e-mail's
   domain, so on any address the screen knows where a whole address in that domain ends and moves
   on to the password, as the team's address already did. The next person at a shared PC, after
   the Sign out button, types the whole e-mail, the password and Enter: 3 steps, 56 keys.
   - **Nothing is looked up.** The device keeps only what its own sign-ins taught it; there is no
     new anonymous endpoint (the ratchet caps them at 12, and there are 12) and no cross-tenant
     function. A new device knows nothing: there the plain address behaves as before.
   - **Nothing readable is kept.** `erp.signInDomains` holds a random salt of the device (128
     bits) and up to 8 salted 64-bit FNV-1a hashes of domains, the most recent first. A typed
     domain can be recognised; no domain, e-mail or name can be read back, and a table of hashes
     made on another device does not apply (another salt). It is a device setting like the
     language (listed in `deviceKeys`, kept across sign-outs, reviewed in
     `tests/Gates/client-carriers.txt`); the client-isolation unit test checks that every kept
     value holds nothing of the signed-out tenant, and the end-to-end test checks the browser's
     storage after the Sign out button.
   - **Said beforehand.** On such a device the plain address shows, under the e-mail field and in
     its description: "On this device, typing a whole address of a workspace you signed in to here
     moves on to the password." (WCAG 2.2 SC 3.2.2 On Input: a change of context the person is told
     about before using the field.)
   - **Habits are absorbed.** A person used to leaving the field with Tab (as in the reference) or
     Enter presses it right after the screen moved on. That key, as the very next key in the still
     empty password field, is absorbed once: it neither skips past the password to the Show button
     nor sends the form without a password. Shift+Tab, Backspace (back to the e-mail's end) and any
     key after the first behave as usual. This applies on the team's address too.
   - Known edge, as on the team's address: a domain that extends a known one
     (`x@alnoor.example.ae` on a device that knows `alnoor.example`) moves on after
     `alnoor.example`; the note said so, Backspace returns, and the sign-in fails with the ordinary
     message.

## Where the password path stands against the reference

| Start | Ours | Reference (password) |
|---|---|---|
| Shared PC after the Sign out button, plain address | whole e-mail (moves on), password, Enter: **3 steps, 56 keys** | e-mail, Tab, password, Enter: 4 steps, 57 keys |
| Shared PC whose last sign-in used a passkey, plain address | e-mail, Enter, password, Enter (the first key withdraws the request): 4 steps, 57 keys; on a device that signed in to the workspace before, 3 steps, 56 keys | 4 steps, 57 keys |
| Team's address, new device | whole e-mail, password, Enter: 3 steps, 56 keys; or part before "@", Enter, password, Enter: 4 steps, 35 keys | 4 steps, 57 keys |
| Returning browser (session ended without Sign out) | password, Enter: 2 steps, 21 keys | the reference's "Choose a user" path: 4 steps, 21 keys |
| Brand-new device, plain address | 4 steps, 57 keys (machine and modelled time lower) | 4 steps, 57 keys |

The last row is still a tie on steps and keys. Fewer steps there would need the screen to know
where an address ends with nothing on the device and nothing in the address, which means asking
the server which domains have workspaces: an enumeration oracle for anonymous visitors (anyone
would learn which companies use the product), a new anonymous endpoint and a cross-tenant lookup.
The ratchet caps the anonymous endpoints (12, all used) and the security-definer functions (2),
and maximums only go down. A team's first sign-in on a new device comes from the set-up
hand-over, which names the team's address (3 steps) and the e-mail (2 steps).

## Tests

- Unit (`SignInPasskey.test.tsx`): typing withdraws the request silently with Sign in enabled and
  the keyboard kept, and e-mail, Enter, password, Enter signs in; a password manager filling the
  field and the form being sent withdraw it; after a password sign-in the next screen does not ask
  and the button does, and a passkey sign-in asks at once again.
- Unit (`SignInPage.test.tsx`, `signInAddress.test.ts`): the plain address moves on only for a
  domain this device signed in with, in both languages with the note first; not for another
  domain, an extending one or with a password typed; the habitual Tab or Enter is absorbed once;
  the hash, the salt, eight domains kept, a stored value it did not write ignored.
- End to end (`sign-in.spec.ts`, `passkey.spec.ts`, real Chromium): the shared-PC path after the
  Sign out button (3 actions, nothing readable kept, Tab by habit still lands in the password, a
  new device does not move on); the critic's shared-device case (a virtual authenticator with no
  passkey of the person, `erp.passkeyOffer=1`): the prompt goes away at the first key and e-mail,
  Enter, password, Enter signs in.

## Comparison harness (unchanged)

`COMPARE_OURS_URL=http://localhost:20020 node run.mjs --task sign-in --product both --repeat 3`
against this branch's `./erp up` and the shared reference rig, 2026-10-10 13:57 UTC: verdict win
(ours judged on the passkey path, 1 step / 2.03 modelled s / 0.06-0.11 machine s against the
reference's 2 / 4.68 / 1.28-1.49; keystrokes 0 in both, left out). Ours' password paths from the
team's address as before: new device 4 steps / 35 keys / 11.55 s, whole e-mail 3 / 56 / 18.78 s,
returning 2 / 21 / 7.63 s. The harness starts ours on the team's address and has no start state
"after the Sign out button on the plain address"; that path is measured by the end-to-end test
above.
