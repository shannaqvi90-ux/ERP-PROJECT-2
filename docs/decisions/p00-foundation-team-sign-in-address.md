# p00 — The team's sign-in address, and Enter in the e-mail field

Date: 2026-10-05. Piece: p00-foundation (round 6). Status: accepted.

## Context

The round-5 critic compared the first sign-in on a new device (nothing remembered) with Odoo's:
both needed e-mail, Tab, password, Enter — 4 steps, 57 keystrokes, 19.06 modelled seconds
(`gauntlet/reference/odoo/tasks/sign-in.json`). Our only advantage was a remembered e-mail, which a
new device does not have. A tie is a loss.

## Decision

1. **The team's sign-in address names the e-mail domain.** `/?domain=demo-trading.example`: the
   sign-in screen shows `@demo-trading.example` after the e-mail field and the person types only
   the part before it (`signin.tester`). Typing `@` switches to a whole address (someone outside
   the domain). The browser keeps the whole e-mail for password managers (a hidden `username`
   field) and the device remembers the whole e-mail after a successful sign-in, as before.
   - Where people get it: every user's **My account** page shows the address for their own
     domain with a Copy button, and the set-up hand-over a manager copies when creating a user
     names it ("Sign in at …/?domain=… with … and the set-up code …"). It is the address a team
     bookmarks; it never names a user.
   - Nothing is looked up. The domain comes from the address only, so the screen reveals nothing
     about which workspaces or users exist (no enumeration oracle, no new anonymous endpoint, no
     cross-tenant read). A value that is not a host name is ignored (`kernel/signInAddress.ts`).
2. **Enter in the e-mail field goes on to the password** when the password is still empty (and
   the e-mail is valid); otherwise it says why, in the screen's language, without a round trip.
   Before, Enter there submitted the form and showed "Enter your password." as an error.

## Effect on the compared task (new device, nothing remembered)

| | Odoo | Ours before | Ours now |
|---|---|---|---|
| Path | e-mail, Tab, password, Enter | same | `signin.tester`, Enter, password, Enter |
| Steps | 4 | 4 | 4 |
| Keystrokes | 57 | 57 | 35 |
| Modelled human seconds (KLM) | 19.06 | 19.06 | 11.55 (K 35, M 1, H 1) |

Enter right after typing continues the unit of action (the harness's KLM rule 1b), so the second
mental-preparation operator Odoo's Tab needs is gone as well as 22 keystrokes.

The number of steps is still 4: a password sign-in on a device that remembers nothing needs the
person to type who they are, move to the password, type it, and send it. Fewer steps would need a
credential that is not a password (a passkey on the device, a link sent by e-mail), which is not
the compared task ("sign in … with the password …"); that is left to the lead's task definitions.

## Harness

`gauntlet/compare/lib/config.mjs`: our sign-in start is the team's address for the task user's
domain (Odoo's names its database the same way). The ours driver reads what the screen shows
(`#email-domain`) and types only what is still needed, then Enter. The guard test's stand-in
sign-in page models both behaviours and the honest-driver test now also asserts 35 keystrokes.
The plants against the real driver (H1, K1) still apply unchanged.

## Why not

- **Suggesting domains from the workspaces on the server**: an anonymous visitor would learn
  which companies use the product.
- **A personal bookmark carrying the e-mail** (`?email=`): Odoo has the same (`/web/login?login=`),
  so it would tie again, and it puts a person's address in browser history and shared links.
- **Space or another printable key to leave the e-mail field**: it hides a step inside typing; the
  harness refuses control characters in typing for the same reason.

## Measured on the owner's PC (2026-10-05)

`node gauntlet/compare/run.mjs --task sign-in --product both --repeat 3` against a fresh
`./erp up` and the shared Odoo reference rig (Odoo's three runs, ours three runs, medians):

| Variant | Product | Steps | Keystrokes | Machine s | KLM human s | Human + wait s |
|---|---|---|---|---|---|---|
| new device (nothing remembered) | Odoo | 4 | 57 | 0.89 | 19.06 | 19.89 |
| new device, team's address | ours | 4 | 35 | 0.35 | 11.55 | 11.74 |
| returning (best per metric) | ours | 2 | 21 | 0.22 | 7.63 | 7.81 |

The first measurement found the returning variant **invalid**: on the team's address the screen
showed a remembered e-mail as its local part (`signin.tester` before `@demo-trading.example`), and
the harness's fair-start check (`lib/start.mjs`) accepts only the task's whole sign-in in a filled
field. The screen now shows an e-mail it already knows whole, with no domain after it — it is the
person's own sign-in, exactly as it will be sent — and the unit and end-to-end tests check that.
The check was not loosened.

### Why the new-device path still has four steps

A password sign-in on a browser that knows nothing needs four separate actions in any product, as
the harness counts them: who you are (a field entry), leaving that field (a key), the password (a
field entry), sending it (a key). Every way to merge two of them was considered and refused:
a printable key that leaves the field hides a step inside typing (the harness refuses the same in
drivers); finishing the field when a known name is complete needs the list of names (an
enumeration oracle for anonymous visitors); a personal address (`?email=`) is matched by Odoo's
`/web/login?login=`; a password that alone identifies a person needs passwords unique within a
team. Ours wins every other measure on that path (22 fewer keystrokes, 7.5 fewer modelled
seconds, a third of the machine time) and every measure on the returning path. Checked on the
rig (Odoo 20, 2026-10-05): after a session ends without signing out, Odoo's sign-in screen opens
with the e-mail field empty and focused; it remembers the last user only behind a "Choose a user"
button next to the field label, which is at least one more action than typing the password.
Odoo 20 also offers "Use a Passkey"; the compared task is a password sign-in, so neither product
is measured with one.
