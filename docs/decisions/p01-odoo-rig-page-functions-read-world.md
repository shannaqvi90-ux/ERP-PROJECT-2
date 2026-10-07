# p01 — Page functions only read, in a world of their own; the screen verify() reads is the clock's (instrument 6)

Date: 2026-10-08. Piece: p01-odoo-rig (round 7). Status: accepted.

## Context

The round 6 critic closed nothing on the Node side (the sandboxed driver process held), but found
the browser side still open:

- **S1, S2, S3 (uncounted actions).** A driver may run a page function: the condition of
  `op.waitFor`, `ctx.until`, or a reader for `ctx.read`. It ran in the page's own script world
  inside a sentinel that patched the page's prototypes for the length of the call and put them back
  after. Anything the function scheduled ran after that: a native `await` continuation (it never
  calls the patched `Promise.prototype.then`), an animation's `onfinish`, a `MutationObserver`. The
  critic's plant in the real ours edit-and-save driver filled the phone and saved with 0 steps and
  0 keys, and the harness suite stayed green.
- **T3 (wrong timer).** The clock stops when `run()` returns. Freezing the page's script
  (`Emulation.setScriptExecutionDisabled`) stops timers but not the continuation of a request
  already under way, so a read answered 2 s later still reached the screen, and `verify()` read it
  in short reads that the meter did not flag. Removing the freeze altogether left the suite green
  (mutation M5).
- Also found while building this: a `javascript:` address set from any script world runs later in
  the page's own world and fires no `navigate` event, so the old sentinel could not have seen it
  either.

## Decisions

1. **A page function is checked in source and must only read** (`checkPageScript`,
   `lib/page-script.mjs`). Refused: async functions, `await`, generators, `import`, `with`, `this`,
   `debugger`; any write to a property and any assignment to a name the function did not declare;
   computed property names other than a number; the names that reach the window, another frame,
   the address object, reflection or code evaluation (`window`, `self`, `globalThis`, `top`,
   `parent`, `frames`, `defaultView`, `contentWindow`, `eval`, `Function`, `Reflect`,
   `.constructor`, `.prototype`, `.then`, the `Object` members that hand out property values,
   `JSON.stringify` with a replacer); `new` of anything but plain data; `location` other than
   `location.pathname` and its other parts. Every existing driver's page functions pass unchanged.
   The driver lint applies the same check to the driver's source, so a bad condition shows in
   review as well as at run time.
   - Why static: some escapes cannot be refused at run time at all. The address object's members are
     unforgeable (they cannot be patched), and `location.href = 'javascript:…'` runs in the page's
     world later, unseen. Keeping the object out of reach in source is the only sure way.
   - Why this narrow: page functions only need to read. Indexing by a variable (`rows[i]`) is the
     one common read it forbids; `.item(i)` or `.at(i)` do the same.
2. **It runs in an isolated world of the harness's**, created through the browser's debugging
   protocol (`Page.createIsolatedWorld`, then `Runtime.evaluate` in that context), never in the
   page's own script world. The product's globals, prototypes and event handlers are out of reach,
   so a function cannot leave a hook the product calls later (a patched `JSON.parse`, say). Only the
   document is shared. Its argument is plain data, written in as JSON.
   - Why not Playwright's `evaluate`: it runs in the page's own world. Playwright has its own
     utility world, but no public way to run a function there.
3. **That world is armed when it is created and never disarmed.** Every method and property
   setter of the browser's interfaces that acts is replaced with a refusal that throws and logs:
   clicks, focus, values, DOM changes, timers, promises (`then`, the constructor and its statics),
   observers, listeners and `on…` handlers, network, storage, history, workers, animations,
   `eval` and the function constructors, and the `Object`/`Reflect` members that copy or describe
   values. The world's prototypes are frozen, so nothing can re-arm or patch them. The log is read
   after every call and at the clock's end; anything there makes the run invalid. So even
   something a function managed to schedule would find every action refused when it ran.
   - Around each call the world still reports any DOM change, event, navigation (cancelled), focus
     move or change of address the call caused, as the sentinel did.
   - Conditions are polled from the harness every 50 ms (Playwright's `waitForFunction` polled in
     the page at the same interval). A function that runs over 10 s (0.5 s in `verify()`) is ended.
4. **When the clock stops, what the page is still loading is aborted** (`Page.stopLoading`, after
   the script freeze), recorded as `requests_in_flight_at_clock`, and new requests stay refused.
   Reads still loading are not waited for (unchanged since round 5): aborting them keeps the clock
   honest without charging either product for a background read it did not need.
5. **The screen is fingerprinted at the clock and after `verify()`.** The fingerprint covers the
   address, every element with its attributes, the text, the fields' values and the focus; it
   leaves out what the blind screenshot itself changes (title, icons, emptied placeholders and
   labels, an empty `style` left by hiding the caret). It is taken after the script freeze and
   before the abort, because a request's failure handler runs as the request is aborted: if it
   writes the end state (plant T6), that is a change after the clock, and the run is invalid. A
   screen that changed for any other reason (a freeze that failed) is caught the same way.
6. **A variant's own hooks run.** The driver process describes each variant with the base driver's
   hooks and the variant's own over them, and the runner calls those. Before, a variant's `setup`,
   `signIn` and `ready` ran only when the base driver had the same hook, so the ours sign-in
   driver's `returning` variant was never set up and was measured as `new-device`.
7. **Instrument version 6.** Polling moved from the page to the harness and the end of the clock
   changed, so every Odoo baseline is re-captured with the new instrument (median of three).

## Self-tests

`test/page-script.test.mjs`: each layer on its own. The source check refuses 40 kinds of acting
function and accepts every driver's. The read world, given sources that skip the source check,
refuses and logs 26 kinds of action with the product's handlers never running, stays armed after a
call returns (S1 at its root: the continuation's click is refused and reported to the harness),
cannot be patched, reports a change it cannot refuse (a data attribute), re-arms in a new
document and ends a busy loop. The freeze is tested directly: a ticker and a 2 s answer stay off
the screen for 2.6 s. End to end through the runner: S1, S2, S3, a `javascript:` address,
`location.replace`, the address object reached by enumeration or through an alias, a condition
that clicks or animates or reaches an iframe's world, a promise-like result and a page object as
argument are all invalid with nothing saved; T3 and T4 (a timer-delayed answer) never verify; T6
is invalid with the screen change named; an honest product with a live ticker still verifies.
The critic's real-driver plant in ours edit-and-save is caught by the lint. The round 6 mutations
M2 (no greyscale) and M4 (no `Socket#connect` lock) now each fail a test of their own
(`test/operator.test.mjs`, `test/sandbox.test.mjs`).

## Consequences

- A driver whose condition needs a variable index uses `.item(i)`; one that needs the window's
  state cannot read it (the product's globals were never a fair thing to read anyway).
- A product whose own code writes the DOM straight from a request's failure handler, while the
  clock-end abort hits one of its background reads, would see an honest run marked invalid. Our
  product (React) and the reference (Odoo's OWL) render through scheduled work that the freeze
  stops, and every Odoo baseline and ours health run passed with instrument 6; a failure of this
  kind names the change in the error, so it would show at once.
