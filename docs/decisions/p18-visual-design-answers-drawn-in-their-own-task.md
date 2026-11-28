# p18: request answers are NOT drawn with flushSync (round 4's commitNow, withdrawn)

Status: withdrawn in round 5 (serial-27b). Date: 2026-11-28.

Round 4 wrapped the state set from a request's answer in `flushSync` (`kernel/commitNow.ts`, used by
`ListView`, `useListRows` and `ImportDialog`) to stop the comparison harness judging `import-5000`
INVALID ("a page task no step caused"). The integrator's pre-check showed it broke other screens'
health runs (see-and-rerun-job, add-rate, configure-sequence, custom-field-filter: "the screen changed
after the clock stopped", "Failed to fetch" in place of the list). The real cause was in the harness
(p01 r15 and r16 attribute follow-up tasks exactly), so the product change was not needed.

Done in round 5: `commitNow.ts` and every use of it are removed, with the round's other changes made
only for it (the width measurement in a layout effect after every render, tab counts asked together,
the jobs list's busy flag). `ListView`, `useListRows` and `ImportDialog` are the integration branch's
versions again. Kept from round 4: titles on cut inner spans, the date box taking a typed slash,
design.spec's inner-span and settings-list checks, the attention-figure test.

Not to be redone without a measured run of the built-ours health check on verify's clean stack that
fails with the harness of p01 r16.
