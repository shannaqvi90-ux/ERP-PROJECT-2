# p00: the comparison harness gives both products the person's passkey device

Date: 2026-10-09 (round 8)

## Context

Both products sign in with passkeys (ours: `p00-foundation-passkey-sign-in.md`; the reference rig
has Odoo's `auth_passkey` installed and its sign-in screen offers "Use a Passkey"). A passkey
sign-in has an action no key or click measures: the person confirms on the device (fingerprint,
face, device PIN) when the browser asks. Chromium's virtual authenticator answers by itself
(automatic presence), which would make that action free and uncounted; with automatic presence
off, a request already made is never answered when presence is switched on later (measured on
this machine's Chromium), so the harness cannot simply hold the authenticator.

## Decisions

1. **A task declares a device** (`device: 'passkey'`, the sign-in task). Every browser of such a
   task gets the same device (`lib/device.mjs`): a virtual authenticator per page (CTAP 2.1,
   internal transport, discoverable credentials, user-verifying, answering at once) and a shim,
   installed by the harness before any page script, that holds every `navigator.credentials` get
   or create with a `publicKey` until the person confirms. The shim replaces the methods on
   `CredentialsContainer.prototype` (non-writable, non-configurable), so the page keeps no
   reference to an unheld method; the binding it waits on only reports a request.
2. **Set-up confirms at once; the measured part waits for a counted step.** In the free phase
   (set-up, sign-in hooks) the device confirms at once: set-up adds a passkey the way a person does,
   on the product's own screens. From the start screen on, a request waits for
   `op.confirmOnDevice()`, a counted action: one step, no keystroke, waiting for the product to ask
   (system wait) and failing when it never asks. A product that asks as its sign-in screen opens is
   asked on the start screen and answered only by that step.
3. **The device follows the person.** What the authenticator holds after set-up (private keys
   included, from CDP `WebAuthn.getCredentials`) is added to the authenticator of the fresh start
   browser, as a person's phone or laptop is the same device after the browser closed.
4. **KLM.** A confirmation is one button press, K (0.28 s), on a device of its own: one H (0.40 s)
   to reach it from the keyboard or mouse, and M before it like every step that does not continue
   the one before (2.03 s alone). Card, Moran and Newell have no biometric operator; K is the
   smallest operator they allow, and the same rule applies to both products. The result counts
   confirmations (`device_confirmations`).
5. **Both products' passkey paths are measured.** Ours: `passkey` variant (set-up: password
   sign-in, My account > Add a passkey, sign out; run: confirm). Reference: `passkey` variant
   (set-up: password sign-in, Preferences > Security > Add Passkey > confirm the password > name >
   Create, sign out; run: click "Use a Passkey", confirm; clean-up removes the passkey again). The
   reference's baseline (`gauntlet/reference/odoo/tasks/sign-in.json`) was taken again with it, so
   the reference's best of each metric includes its passkey path (2 steps, 0 keystrokes, 4.68 s).
6. **Instrument mutations M21 to M23** plant the device's faults (it answers without the person once
   the clock runs; a confirmation modelled as free; the shim on the container instead of its
   prototype) and each is caught by `test/operator.test.mjs`.

## Processor time

The three device tests run in the existing operator test file (its browser): about 1.7 s of wall
time, under 3 s of processor time. M21 to M23 share operator.test.mjs's existing control run; each
mutated run starts one browser (about 6 s of processor time each, measured on this machine). The
`passkey` variant adds one sign-in-with-set-up run to the verify's health check of the ours drivers
(about 4 s).
