# p03 — Full-size permission boxes, record commands on every tab, account actions that keep the form saveable

Date: 2026-10-17. Piece: p03-identity (round 11). Status: accepted.

## Context

Critic p03 round 10 (verdict WIN) named as its biggest gap that the permission matrix's "Other"
column (Reset other users' passwords, Share list views with everyone; later post journal, approve,
close period) drew each checkbox as a 5 to 12 px dot beside its wrapping label, against 13 px for
every other box, so the riskiest grants were the hardest to see and click. Its other findings: an
unblock from the user's own record left the form on a stale version, so the next save was refused
as "changed by someone else"; no gate covered a palette action's `requires`; the takeover gate
crashed on a 401 instead of naming the endpoint when a plant ended the Administrator's session;
the record's palette commands followed the Details tab; a copied role was "X (copy)" in English but
"X (2)" in Arabic; the all-keyboard create-restricted-user path tied Odoo on keystrokes.

## Decisions

1. **A checkbox or radio is never squeezed.** The cause was the field rule `input { min-width: 0 }`
   (kernel styles) plus the default `flex-shrink: 1`: in the matrix's inline-flex label the box gave
   way to the wrapping text. The shared rule for checkboxes and radios now sets `flex: none;
   min-width: auto` (additive, for every module), and the matrix cell aligns the box with the first
   line of its label. Gate: an end-to-end test measures every box of the matrix (a new role, and
   the Administrator's ticked read-only one) and of the role picker at 1280, 1600 and 1920 px, in
   English and Arabic: each must be at least 12.5 px square and all the same width, and boxes of
   the "Other" column must be among those measured. With the old rule injected it fails on every
   screen and width (6.5 to 9.9 px boxes).

2. **Account actions adopt the user's new version, not the form's draft.** Allow sign-in again
   writes the user's row (`sign_in_unblocked_at`), so its version moves. After an unblock, a
   password reset, Sign out everywhere or a passkey removal, the record is read again; when every
   field the form edits is unchanged the form takes the new copy (`adopt`, which keeps what is
   being typed). When a field changed, someone else changed it, and the form keeps the version it
   read so the save still reports the conflict. The server is unchanged: the version is the
   row's, and the unblock is a change to the record that the audit trail shows. Tests: unblock then
   untick Active saves with the new version; the control (renamed elsewhere meanwhile) is still
   refused.

3. **A record's commands follow the record, not the tab.** `FormTabs` (kernel forms) takes an
   optional `current`/`onSelect`, additive. On a user's other tabs the panel offers Reset
   password, Sign out everywhere and Delete user under exactly the conditions their buttons have;
   running one opens Details and does what the button does (the reset's first choice focused,
   Delete asking first). Gates: the palette test offers them from every tab to an administrator and
   to a reader none; plant PA12 (the tab-level Reset offered without its condition) is caught.

4. **`requires` has a gate.** For every module action that asks for a record (Copy role, Show the
   sign-in history, Allow sign-in again), and every permission of its module besides its own and its
   screen's, a user holding everything but that permission is either not offered the action or the
   action runs to its end on a record no check of its own refuses (a user holding no role, a role
   granting nothing). 26 such runs today (ratchet `g2.paletteRequiresRuns`). Plant PA11 (the
   critic's U1: Allow sign-in again without `requires: ["identity.signIns.read"]`) is caught.
   Only record actions are swept, to keep the processor time down: the other actions open a list
   and run a list command, which their screen's own permission already decides.

5. **The takeover gate checks the Administrator's session before reading their record.** A request
   that ends it is reported as "`<endpoint>`: the Administrator's session ended", with their
   passkeys compared in a fresh session of theirs, instead of stopping on the next read's 401.
   The self-test plant for `?removePasskeys=true` now also ends the path user's sessions, as the
   critic's S1 did; without the reordering the self-test fails with the 401 the critic saw.

6. **A copied role is named in each language in that language**, whatever the screen's language:
   "X (copy)" and "X (نسخة)".

7. **Alt+O moves to the role finder** on a user form (new or existing), listed in the shortcut
   sheet; the finder's hint says so in both languages. O is matched by key position, so it works
   with an Arabic layout. Alt+R was taken (print). The comparison driver's keyboard variant uses it
   in place of three Tabs: 7 steps and 45 keystrokes against Odoo's 10 and 46 (measured on this
   branch's demo, ours only; the pointer path is unchanged at 5 steps and 37 keystrokes).

8. **The paused sign-in, end to end in the browser.** p00's critics (rounds 7 to 10) found the
   right password after repeated failures answered only "Sign-in failed"; round 10 changed the
   server and screen (`p03-identity-sign-in-pause-said-plainly.md`) after p00's round 10 commit was
   judged. An end-to-end test now drives it: a fresh account, its own browser, failures until the
   pause, then the right password shows "paused until … ask your administrator to allow sign-in
   again" and not "Sign-in failed", then the same in Arabic after the switch; the administrator's
   Allow sign-in again lets the right password in from that browser.

## Processor time

This branch's verify (slot taken 2026-10-17T11:59:19Z) passed in 3,468 s wall time, under load (up
to 45 on 16 CPUs). Processor time was 9,272 s against the maximum of 10,500: dotnet 7,180, web
1,657, e2e 385, timing 50. Counts: .NET 629, web unit 485, e2e 105, compare 390, build 6.

What this round adds, measured locally:
- Web unit tests: about 14 s (the requires sweep 10 s; the any-tab, copy and unblock tests 4 s).
- Palette plant self-test: about 25 s. Its control run repeats the sweep; PA11 and PA12 stop at
  the first failure.
- Identity plant self-test control: about 1 s.
- End-to-end: about 6 s of browser time. The paused sign-in test also waits on the sign-in budget,
  which is wall time, not processor time.
- .NET: under 1 s.

That is about 45 s in all. The run as a whole stayed below the round-10 figures (9,943 s)
because other agents' load varies.
