# p00: passkey sign-in, asked for as the sign-in screen opens

Date: 2026-10-09 (round 8)

## Context

Round 7's critic: ours beat the reference on sign-in only from the team's sign-in address
(`/?domain=…`, 3 steps / 56 keys against 4 / 57). From the plain address, a whole e-mail does not
move on by itself (4 / 57, a tie), and a personal bookmark that carries the e-mail is a tie too
(password, Enter: 2 / 21 in both). The reference rig has Odoo's own passkey module installed: its
sign-in screen offers "Use a Passkey" (a click, then the confirmation on the device).

## Decisions

1. **Passkeys (WebAuthn) in the identity module.** `identity.passkeys` (tenant table, row-level
   security forced, audited; the usage columns `last_used_at`, `sign_count`, `last_challenge_at` are
   left out of the audit change set). A signed-in user adds, renames and removes their own passkeys
   on My account; an administrator lists another user's and removes them all (a lost device, a
   person leaving), within the same "never on a stronger user" rule as other user actions.
   ES256 and RS256 keys, attestation `none`, user verification required, a discoverable credential
   (the person picks the account on the device, no e-mail typed). At most 20 per user. Adding needs
   a sign-in within the last 10 minutes; otherwise My account asks for the password first.
2. **Stateless challenges.** The anonymous `GET /api/auth/session` answer carries a fresh sign-in
   challenge (purpose, issue time, random bytes, binding, MAC with a key derived from the
   deployment's device key). No table of outstanding challenges; a challenge lives 5 minutes; each
   passkey keeps the issue time of the last challenge it answered and refuses that one or an older
   one (replay), and a signature counter that goes backwards is refused. The user handle is the
   workspace id and the user id, so a sign-in knows where to look before any workspace is bound;
   a handle naming another workspace than the credential's answers the same refusal as an unknown
   credential (gate G1PasskeyTests).
3. **The screen asks at once on a device that uses a passkey.** The device setting
   `erp.passkeyOffer` (localStorage, "1"/"0", names nobody) is set when a passkey is added or used
   on the device, and can be switched off on My account. With it, the sign-in screen asks the device
   for a passkey as it opens, from any address (plain, the team's, a personal bookmark): the person
   confirms on the device and is in. Signing in is then one action (the confirmation), where the
   reference needs two (click "Use a Passkey", confirm). Cancelling leaves the password path, with
   the keyboard in the e-mail field (or the password, when the e-mail is filled in).
4. **Not right after Sign out.** The Sign out button marks the tab (`erp.signedOut`,
   sessionStorage, read once): the next sign-in screen of that tab does not ask (the person just
   left; on a shared device the next person should not meet a passkey prompt). The button
   "Sign in with a passkey" is always there; a later visit (a new tab, the next morning) asks again.
5. **Repeated failures say that sign-in may pause** (round 7 finding): after three failed password
   sign-ins on the screen, the message adds that sign-in from the device pauses for a while, with
   what to check, still without saying whether the account exists.

## Alternatives not taken

- **Move on to the password when the typed e-mail matches a known workspace domain.** It would
  need an anonymous lookup of workspace domains, telling anyone which companies use the product.
- **Ask every device at once.** On a device without a passkey the browser shows its own dialog
  (another device, a QR code) on every visit, before the person can type a password.
- **Conditional mediation (passkeys in the e-mail field's autofill) as the first path.** It needs
  the person to open the autofill list and pick the entry: more actions than the reference's button.

## Comparison (harness, sign-in task, passkey paths, this machine, 2026-10-09)

| | steps | keystrokes | modelled human s | machine s |
|---|---|---|---|---|
| ours (the screen asks as it opens; confirm) | 1 | 0 | 2.03 | 0.10 |
| reference (Use a Passkey; confirm) | 2 | 0 | 4.68 | 1.12 |

Keystrokes are 0 in both: left out under the owner's zero rule (needs-human #11). The harness
counts the confirmation on the device as a step for both products (decision
`p00-foundation-harness-passkey-device.md`).
