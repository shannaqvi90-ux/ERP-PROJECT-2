# p00 — Tab goes round the page with no dead stop

Date: 2026-10-11. Piece: p00-foundation. Status: accepted.

## Context

Critics of p00 rounds 7, 8 and 9 found the same keyboard fault in the shell: Tab after the last
navigation link ("My account") moved the focus to the document body before "Skip to content",
one dead stop on every round of the page (40 presses: two stops on BODY). In a browser with
its own toolbar the stop is the browser's chrome; a keyboard-first business screen should not
make a person press through it to get back to the first control.

## Decision

- `web/src/kernel/focusCycle.ts`, installed once in `main.tsx` for every screen (sign-in and
  shell): Tab on the last control the browser would visit goes to the first; Shift+Tab on the
  first goes to the last. Between those, Tab is left to the browser.
- What counts as a control is what the browser's Tab visits: links and areas with an address,
  enabled buttons and form fields (not hidden inputs), summaries, editable content and anything
  with a tabindex of 0, visible (`checkVisibility`) and not inside `hidden` or `inert`; a radio
  group is one stop.
- It stands aside when it cannot know the order or is not its business: a positive tabindex
  anywhere (the browser decides), a modal dialog open (`kernel/dialog` cycles inside it), a Tab
  already handled, Ctrl/Alt/Meta+Tab and composition.
- Leaving the page stays possible with the browser's own keys (F6, Ctrl+L to the address bar),
  so it is not a keyboard trap.

## Tests

`web/src/kernel/focusCycle.test.ts` (9 unit tests) and `tests/e2e/specs/keyboard-cycle.spec.ts`
(70 Tab presses round the shell in English and Arabic with no stop on BODY, each round the same
controls in the same order, Shift+Tab the same way back; the sign-in screen; the palette dialog
keeps its own cycle).
