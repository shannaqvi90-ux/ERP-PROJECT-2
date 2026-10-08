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

## Also on the sign-in screen (round 6)

- **Show/Hide the password**: a button joined to the end of the password field (the next Tab stop
  after it; Space or Enter on it toggles, and the focus goes back to the field), so a person can
  check a long password before sending it. The field's label is its own `<label for>`, so the
  button never becomes part of the field's name.
- **Caps Lock is on**: said under the password field (a status message the field refers to) while
  a key event in the field reports Caps Lock, gone when it is off or the field loses focus — a
  wrong-case password is the commonest failed sign-in and each failure counts toward the lockout.

Odoo's screen has both; ours had neither. Neither changes the measured path (no step, no key).

## Round 6, after the merge: where a password sign-in can and cannot beat Odoo (2026-10-06)

The round-5 verdict asks for the new-device path to beat Odoo on steps too. Checked again
against the harness's rules and Odoo 20's own sign-in screen, without changing either:

- **New device, team's bookmark** (nothing remembered, the address names no user —
  `lib/start.mjs`: "it never names the user"). Two unknown values (who, the password) need two
  field entries, one move between them and one send: four steps in any product. Ours: 4 steps,
  35 keystrokes, 11.55 modelled seconds; Odoo: 4, 57, 19.06. The step tie is at the floor.
  Every way below it either names the user before the clock (a list of the team's people in the
  address or on the screen, `?email=`), needs a server list of people for anonymous visitors (an
  enumeration oracle, and a new anonymous endpoint and cross-tenant function, which
  `gauntlet/ratchet.json` caps at 12 and 2 — maximums only go down), hides a step inside typing,
  or is not a password (a passkey; Odoo 20 offers "Use a Passkey" too, and it would need the same
  anonymous endpoint and cross-tenant lookup).
- **Returning browser** (the last session ended without the Sign out button). Ours: the e-mail is
  remembered and the password has focus — 2 steps, 21 keystrokes. Odoo 20 remembers the last
  users in the browser too (read on the rig, `web/static/src/core/user_switch`): with one
  remembered user its screen shows an empty e-mail field and a "Choose a user" link in the field's
  label; choosing (click the link, click the user) fills the e-mail and focuses the password:
  4 steps, 21 keystrokes (password and Enter). Ours wins steps and time; keystrokes tie at the
  floor (the password itself), and a click on the Sign in button instead of Enter takes both
  products to 20.

So a password sign-in that types the same password in both products cannot be strictly lower on
every measure in either start state; the floor ties are structural, not a missing feature. The
harness's present Odoo `returning` variant types the e-mail instead of choosing the remembered
user, which an expert would not do; with best-per-metric scoring that makes Odoo's keystrokes
57 instead of 21. This is recorded here so no win rests on it; changing the reference driver is
the harness owner's (p01) call, and whether a tie at the floor counts as a loss, or whether the
compared task admits passkeys, is the owner's (raised by this round's builder as a human gate).

## Round 6, resumed: the whole team e-mail ends the field (2026-10-06)

The floor above was wrong in one place. "Finishing the field when a known name is complete needs
the list of names" is true of the **part before "@"**, but not of the **whole address**: on the
team's address the screen already knows the domain, from the address alone, so once the field
holds `someone@<team domain>` nothing more can follow and the screen can leave the field itself.
No list, no lookup, no request: the same information the domain fill-in already uses.

**Decision.** On the team's sign-in address, when the e-mail field *becomes* a whole address in
the team's domain (one `@`, a non-empty part before it, no spaces, the domain exactly the
address's, any letter case) and the password is still empty, the focus moves to the password
(`completesTeamEmail` in `kernel/signInAddress.ts`, `onEmailChange` in `SignInPage.tsx`).

- **Said beforehand, on screen.** A note under the field on the team's address, in English and
  Arabic: "Typing your whole address moves on to the password." It is part of the field's
  description (`aria-describedby`), so a screen reader says it on arrival. Moving the focus on
  input is a change of context that WCAG 2.2 SC 3.2.2 (On Input) allows when the person is told
  before using the field; the note is that notice. The field's name stays "E-mail"
  (`aria-labelledby` its label alone, so the domain and the note describe it, never rename it).
- **Easy to undo.** Backspace in the still-empty password, right after the screen moved on, goes
  back to the end of the e-mail (and only then; elsewhere Backspace is an ordinary key).
- **Never over typed text.** It does not move on when the password already holds text, when the
  address was already whole (editing it), on another domain, or on the plain address (no domain
  is known there, so the screen cannot know where an address ends).
- **Both paths stay.** The part before "@" then Enter is still the path with fewest keys; typing
  the whole address, as most people do by habit, is now the path with fewest steps.
- **Known edge.** A person whose own domain *extends* the team's (`x@alnoor.example.ae` on the
  address of `alnoor.example`) is outside that team's domain by definition, but if they type on
  that team's address the screen moves on after `alnoor.example`. The note said so, the e-mail
  stays visible, Backspace returns, and a sign-in sent that way fails with the ordinary message.

**Effect on the compared task** (new device, team's address; the harness's KLM rules):

| Path | Steps | Keystrokes | KLM human s |
|---|---|---|---|
| Odoo: e-mail, Tab, password, Enter | 4 | 57 | 19.06 (M 2, K 57, H 1) |
| Ours: part before "@", Enter, password, Enter | 4 | 35 | 11.55 (M 1, K 35, H 1) |
| Ours: whole e-mail (the screen moves on), password, Enter | 3 | 56 | 18.78 (M 2, K 56, H 1) |

The harness counts each product's best verified path per metric (`lib/runner.mjs`), so on a new
device ours is now strictly lower than Odoo on steps (3 < 4), keystrokes (35 < 57) and modelled
time (11.55 < 19.06), with machine time measured. The whole-e-mail path alone is also strictly
lower on every counted measure (3 < 4, 56 < 57, 18.78 < 19.06): no measure rests on combining
paths. The password path the person moves on to starts a new mental step in the model (no key
joins the fields), which is why its modelled time is close to Odoo's.

**Harness.** `drivers/ours/sign-in.mjs` has a third path, `new-device-whole-e-mail`: it types the
whole e-mail and then *waits* for the password field to have focus (5 s); if the screen did not
move on, the run fails and the password is never typed into the e-mail field. The guard test's
stand-in moves on the same way and the honest-driver test asserts 3 steps and 56 keystrokes for
that path (4 and 35 for the others, unchanged); a new test runs the path on the plain address,
where nothing moves on, and checks it fails after one step.

**Measured on the owner's PC (2026-10-06, load average 90-150 from other agents' runs)**:
`COMPARE_OURS_URL=http://localhost:20000 node gauntlet/compare/run.mjs --task sign-in --product both --repeat 3`
against `./erp up` of this branch and the shared Odoo rig. Verdict **win**. Each new-device path,
three runs each (machine seconds are the range of the three):

| Product, path | Steps | Keystrokes | Machine s | KLM human s | Human + wait s |
|---|---|---|---|---|---|
| Odoo, new device | 4 | 57 | 2.41-3.19 | 19.06 | 21.29-22.04 |
| Ours, new device, part before "@" + Enter | 4 | 35 | 0.43-0.95 | 11.55 | 11.85-12.35 |
| Ours, new device, whole e-mail (moves on) | 3 | 56 | 0.52-1.09 | 18.78 | 19.09-19.59 |
| Ours, returning | 2 | 21 | 0.31-0.88 | 7.63 | 7.83-8.43 |

The whole-e-mail path alone is strictly lower than Odoo's new-device path on all five measures.

## Round 6, finished: measured again at ordinary load (2026-10-07)

`COMPARE_OURS_URL=http://localhost:20000 node run.mjs --task sign-in --product both --repeat 3`
against `./erp up` of this branch (merged with the integration branch at `ff65952`) and the shared
Odoo rig, load average about 80 (three verify slots busy). Verdict **win**. Per path, three runs
each (machine seconds are the range):

| Product, path | Steps | Keystrokes | Machine s | KLM human s | Human + wait s |
|---|---|---|---|---|---|
| Odoo, new device | 4 | 57 | 1.99-4.34 | 19.06 | 20.89-23.10 |
| Odoo, returning (the harness's path) | 4 | 57 | 1.99-2.83 | 19.06 | 20.85-21.70 |
| Ours, new device, part before "@" + Enter | 4 | 35 | 0.33-0.49 | 11.55 | 11.74-11.87 |
| Ours, new device, whole e-mail (moves on) | 3 | 56 | 0.49-0.51 | 18.78 | 19.01-19.08 |
| Ours, returning | 2 | 21 | 0.27-0.40 | 7.63 | 7.82-7.93 |

The round-5 verdict's tie (new device, 4 / 57 / 19.06 on both sides) is gone on every measure:
with nothing remembered, ours takes 3 steps (whole e-mail) or 35 keystrokes and 11.55 modelled
seconds (part before "@"), against Odoo's 4, 57 and 19.06, and a quarter of the machine time. The
whole-e-mail path alone is lower than Odoo's on all five measures, so no measure rests on combining
paths. Neither path depends on anything the device remembers.

The earlier note above that the step tie at the floor would go to the owner as a human gate is
withdrawn: the floor was not where it was thought to be, and no gate is needed for this task.

## Round 6, resumed after the stop: checked again (2026-10-07, 18:21 UTC)

After merging the integration branch at `6a5e371` (no product or harness code changed in that
merge), `COMPARE_OURS_URL=http://localhost:20000 node run.mjs --task sign-in --product both --repeat 3`
against `./erp up` of this branch and the shared Odoo rig, load average about 1. Verdict **win**,
strictly lower on all five metrics, no tie at zero:

| Metric (median run, best verified path per metric) | Ours | Odoo |
|---|---|---|
| Steps | 3 | 4 |
| Keystrokes | 35 | 57 |
| Machine s | 0.393 | 2.634 |
| KLM human s | 11.55 | 19.06 |
| Human + wait s | 11.741 | 21.326 |

Ours' three runs: machine 0.342-0.492 s; Odoo's: 2.465-2.637 s. The decision above stands.

## Round 6, after the second relaunch: checked again (2026-10-07, 23:14 UTC)

After merging the integration branch at `554fb91` (only verdict records and the run log came
in), the same command against `./erp up` of this branch (port 20000) and the shared Odoo rig, at
load average 57 (other agents' verifies running). Verdict **win**, strictly lower on every metric:

| Metric (each of three runs) | Ours | Odoo |
|---|---|---|
| Steps | 3 | 4 |
| Keystrokes | 35 | 57 |
| Machine s | 0.462-0.611 | 2.250-2.675 |
| KLM human s | 11.55 | 19.06 |
| Human + wait s | 11.791-11.874 | 21.002-21.443 |
