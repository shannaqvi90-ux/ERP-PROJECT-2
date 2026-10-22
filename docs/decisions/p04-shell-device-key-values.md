# p04-shell: the device keys may hold only their setting (round 13)

## Context

The shell keeps five localStorage keys on the device when an identity ends, by design: the
interface language (`erp.language`), the digits (`erp.numerals`), the navigation pane
(`erp.navOpen`), the passkey offer (`erp.passkeyOffer`) and the salted sign-in domain hashes
(`erp.signInDomains`). The next person on a shared device inherits them, and that person may belong
to another tenant.

The round-12 critic planted CL3b. It reused the existing `getItem` and `setItem` of
`erp.language` so that the key also held the address of the record the language was switched on
(`ar|/identity/users/<id>?q=<e-mail>`). After the next sign-in the shell opened that address. The
plant added no storage call and no module-level state, so the static carrier and module-state
gates passed it. Both browser journeys (the web unit one and the e2e one) passed it too, because
neither ever pressed Alt+L, the language button or the digits action while a record was open. At
run time it was a real cross-tenant leak: Gulf Steel's admin landed on an Al Noor user's record.

## Decision

1. **A reviewed list of device keys with value patterns**: `tests/Gates/device-keys.txt` lists each
   kept key with a whole-value pattern (`^(en|ar)$`, `^(latn|arab)$`, `^(0|1)$`, `^(0|1|2)$`, and
   the salt and at most eight 16-hex hashes). Three gates read it:
   - `G1ClientStateTests.The_device_keys_kept_across_sign_out_are_exactly_the_reviewed_ones_and_hold_only_their_setting`
     checks that `deviceKeys` in `web/src/kernel/deviceState.ts` is exactly the reviewed list. Every
     pattern must be anchored, must accept a sample setting, and must refuse that setting with an
     address, an e-mail or a workspace code beside it. A self-test plants a seventh key
     (`erp.lastPlace`) and shows that a widened pattern would let CL3b's value through. Ratchet:
     `g1.clientDeviceKeysShaped` = 5.
   - `web/src/modules/shell/clientIsolation.test.tsx` and `tests/e2e/specs/client-isolation.spec.ts`
     judge every device key's value against its pattern after every step of B, on the signed-out
     screen and after every step of A. This is a value check, independent of markers: a key holding
     anything beyond its setting fails, whether or not a marker is in it.
2. **The journeys run the shell's own switches with a record open.** Right after the record opened
   from the palette, both journeys change and then restore every device setting from every place
   the shell offers it: Alt+L twice, the top bar's language button twice, the palette's digits
   action twice, language and digits in My preferences (Alt+P), and the navigation pane (Alt+B)
   twice. Both tenants take the same steps (26 per tenant end to end, up from 16).
3. **A starts on Home.** After A signs in on the tab B left, A's first address must be `/` with no
   query. CL3b put A on B's record with B's e-mail in the search box.
4. **The unit journey uses UUID-shaped ids.** A client that treats an id by its shape (CL3b only
   kept addresses ending in a UUID) runs in the test the way it runs in the product. With the old
   ids (`ub-canary-7q4`) the plant never fired in the unit test.
5. **CL3b is a self-test plant** in `web/scripts/plant-self-test.mjs`. That script now runs 13
   plants, and every one is caught. I also applied the critic's diff to a running stack: the e2e
   journey failed on the device judge and on B's markers in localStorage after sign-out.
6. **Product defence**: `forgetIdentity()` also drops a kept device key whose value is not exactly
   its setting (`deviceValueKept`), so a stray value is not handed to the next person. This is a
   second layer only. A plant that edits the product could bypass it, which is why the gates above
   judge the values on their own.

## Other round-12 findings closed

- **Help sheet**: a "Forms" section is always listed: Enter in a one-line field, Ctrl+S and
  Ctrl+Enter to save, Alt+Z to discard, Alt+PageDown and Alt+PageUp for the next and previous
  record, Alt+R to print, Esc to close, and Alt+O for the user form's role filter. While a form is
  open its own registrations replace the static rows, so no key is listed twice. Unit test in
  English and Arabic.
- **Print with a record open**: the record now prints first, and the list rows follow it on the
  same paper, after the printout's scope note. A forced page break before the list left a blank
  page whenever the record ran a few pixels past a page, so there is none. The user form's keyboard
  hints ("Alt+O comes here…", "Ctrl+Enter saves, Esc closes") are marked `no-print`.
  `print-paper.spec.ts` now also checks that the record is on paper before the list rows and that
  no `Alt+`/`Ctrl+`/`Shift+` hint is printed, in English and Arabic.
- Not changed: the G1ShellAnswers SL2 note. G1NonInterference and the static outside-process scan
  catch SL2, and the shared machinery belongs to p00. Also not changed: the thin modelled margin on
  reach-screen-keyboard. Ours is G then U, two keystrokes, against Odoo's eight. Every metric
  wins, and one keystroke would need a single-key global shortcut to one screen, which would take a
  plain letter away from typing.

## Processor time this round adds

- Web unit: each of the two B-then-A journeys takes about 2.5 s longer (about 23 s each, measured
  alone). The new self-test takes 0.1 s and the help-sheet test 0.3 s. The plant self-test gains
  one plant run, about 8 processor seconds, and each of its 14 runs is about 5 s longer: roughly
  80 processor seconds in all.
- e2e: the G1 journey has 13 more steps per tenant, about 3 s. The print check adds no new page
  loads.
- .NET: two file-only gate tests, under 1 s.
- In total, under 100 processor seconds against the 10,500 s maximum.
