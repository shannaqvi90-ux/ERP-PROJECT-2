# p01: a write counts only when a counted step caused it; an entered value only when typed

Round 11 of p01-odoo-rig. Instrument version 9.

## The gap (critic p01 round 10, verdict BLOCKED)

Round 10 required that a task which saves sends one of its declared writes during the measured
part, and that every entered value the end state gained was "typed, picked as a file, or sent in one
of its writes". Both rules looked at what the browser page sent, not at what a counted step caused.
Set-up may write to the product before the clock, so it could leave the task's work to the start
page's own code:

- R1: set-up stored the value as a server-side draft. The start page showed it as text (the
  start-screen check reads fields only) with a "Save draft" button; `run()` clicked once. The page's
  write carried the value, so it counted as entered: verified with 1 step and 0 keys.
- R2: set-up turned on a setting; the start page sent the declared approval write from its own timer
  3 s after it opened. `run()` pressed Escape and waited: verified with 1 step and 1 key.
- R3: both together: the start page autosaved the stashed value on a timer.

## Decision

Two rules, both in `lib/runner.mjs`:

1. **An entered value counts as entered only when the measured part typed it** (its typed text, in
   one piece or several; the typed pieces are joined, so a product that offers the start of a value
   and the person types the rest still counts), **or picked it as a file**, or, for an API task,
   typed it into a request. Its presence in a request body the page composed no longer counts. The
   refusal names the value and says when the page sent it in a request it composed itself.

2. **A page's write counts as the measured part's only when a counted step caused it**
   (`causedWrites`). The runner records, for every request the measured page sends, when it went out
   and when its answer began, on the clock. A write is caused by a step when it went out
   - while the step was being performed, or within `REACTION_SECONDS` (1 s) after it ended, or
   - within 1 s after the answer to a request that went out while a step was being performed (one
     link, never more).
   The measured part must have caused a write, and (when the task declares its writes) one of the
   declared writes. Each write is recorded with its cause in `saved_state.writes_caused` (the step,
   the answer it followed, or how long after which step it went out).

Instrument version 9: every Odoo baseline was captured again (median of three on the shared rig).

### Why one second, and one link

Measured on the real drivers in this round (`saved_state.writes_caused` of the runs):

- Our product: every save went out before the click or key had finished (lag after the step's end
  from -0.008 s to +0.007 s); the second write of create-restricted-user (the access grant, sent once
  the user exists) went out 0.23-0.36 s after the step, on a machine at load 80.
- The reference (the baselines of this round, `saved_state.writes_caused`): every declared write
  went out during its step or within 0.06 s of its end, bar add-rate's save of the rate line, 0.31-
  0.32 s after the key or click that saves it; other writes up to 0.35 s after a step (a
  dashboard's call 0.63 s after a menu click), at load 15-55. Its form saves once a pending onchange is
  answered, which is the one link; the import's later batches continue the first (below).

A page answers a key or a click at once; its framework may take a few event-loop turns (a scheduler
task, a microtask chain) or wait for one answer it asked for first. One second leaves a wide margin
for a loaded machine. A timer the page set before the clock fires at a moment no step chose; the
critic's R2 timer fired 2.3 s after the key. More than one link would let a poll carry a write
arbitrarily far from any step (each poll's answer would link the next one), so the link must start
inside a step.

### Why not the critic's other options

- *Read Chromium's request initiator (async stack traces).* Async stacks need the page's debugger
  enabled for the whole measured part (a cost on both products' machine seconds), and the stack does
  not name the input event that started a chain; a framework's scheduler task looks like any timer.
- *User activation (`hasUserGesture`).* Chromium keeps a transient activation for 5 s after an input
  (Escape does not activate), so it is a coarser form of the same timing rule.
- *Read the start screen's text, not only its fields, for entered values.* Records keep earlier runs'
  values in their history (a chatter line "phone: old -> new"), so the check would refuse honest runs.
  With rule 1 it is not needed: a value shown on screen still has to be typed, at its full cost.
- *Refuse a set-up write whose body carries an entered value.* Honest set-up does that (our
  create-company-branch driver renames earlier copies "<company> (retired ...)"), and a stash can be
  encoded. Rule 1 makes it unnecessary.

## Routed to p01 in this round

- **Odoo set-up noise** (p00 r9 and r10: the sign-in driver's 'returning' variant; p02 r10:
  create-company-branch). Playwright reports a frame navigated both when a new document replaces the
  old one and when the page only changes its address (`history.pushState`). Odoo changes its address
  as an action loads (`/odoo` -> `/odoo/contacts`), and set-up's watch took that for the page moving
  on: the sign-out's `POST /web/session/logout` and Odoo's silent `res.company/iap_enrich_auto` were
  recorded as abandoned while still under way, and the run refused. Now only a new document (a
  document request for that frame went out since its last navigation) abandons a page's requests.
  The same mistake inside the clock dropped a save still under way from the writes the clock waits
  for, which stopped the clock early (in favour of whichever product changes its address while it
  saves); it is fixed the same way (control C4, mutation M60).
  Set-up may also wait for its own writes before moving its page on: `ctx.settleWrites()` (set-up
  and sign-in only, refused once the clock runs). The Odoo drivers' `settle()` calls it after every
  sign-in, and the sign-in driver before and after its sign-outs. Nothing about what the instrument
  counts or accepts during the clock changed: a write set-up abandons still refuses the run, and the
  runner still waits for set-up's writes before the start.
- **Odoo's emoji data after the clock** (p04 r9). Once Odoo's chatter renders a message it loads its
  emoji data: a bundle request, then a script element it adds to the page from that request's
  answer. Disabling the page's script at the clock does not stop a promise continuation already
  pending (tried in this round: script disabled, a frozen lifecycle, network throttling and offline
  emulation all let a late answer change the page; pausing the debugger stops it but also stops the
  harness's own reads and screenshots). So the run's page changed after the clock and the run was
  refused, correctly. The Odoo drivers whose last screen is a record form opened at the end now wait
  for it on the clock: `emojiSettled` (a message already shown has its emoji data in; measured cost
  0.001-0.008 s on find-record, find-user, keyboard-navigation and open-third-user-keyboard) and, for
  a new record whose first message renders with the form, `chatterSettled` (the thread loaded and
  its emoji data in: about 0.09 s on new-user-keyboard). A heavier wait for every chatter to load was
  measured at 0.37 s on find-record and not taken: it would have charged the reference for loading it
  does not need to finish.
- **Critic-added tasks registered**: `new-user-keyboard` and `open-third-user-keyboard` (p04 critic,
  drivers in `gauntlet/evidence/p04-shell/r9`). Odoo's side keeps the shortest keyboard path found on
  the rig (command palette, "/user", Enter; then Alt+C, or Down x4 and Enter: the first Down reaches
  the header row). Ours offers the go-to keys and the navigation pane as whole paths.
- **The shared rig's job runs**: the 100,000 check (`lib/rig-volume.mjs`) is unchanged.

## Other findings of round 10 closed

- **X1** (the request-failed path of set-up's watch removed was missed by the self-tests): a test now
  requires a write the page aborted to be named as abandoned with the browser's reason
  (`net::ERR_ABORTED`) and without waiting out the timeout (mutation M58).
- **Long-lived channels by address**: any address containing "websocket", "longpolling" or "/bus/"
  (its query included) was left unwatched and uncounted. Odoo 20's bus is a web socket; its HTTP
  routes beside it end like any request. Long-lived channels are now known by the kind of request the
  browser reports (web socket, event stream) only (mutation M61).

## Limits

The instrument cannot tell a product's own answer to a key from an answer set-up configured: a
setting that makes the start page save as soon as any key is pressed sends a write the key caused.
A timer that the key arms for longer than the reaction time is refused (plant R2b, and R2c with the
page polling meanwhile). A product that saves on a timer of its own after typing stops (an autosave
debounced by more than a second) would be refused too; neither product does. Tasks and drivers stay
reviewed code.

## Tests and mutations

- `test/measured-part.test.mjs` (13 tests): controls C1-C4 (a save sent at once; a few event-loop
  turns later; after a slow check's answer; while the page changes its address), plants R1, R2, R2b
  with R2c, R2d, R3, and set-up's watch (X1, address change against a new document, a channel-like
  address, `ctx.settleWrites()` refused inside the clock). `compare.measuredPartPlants`: 13.
- `test/before-clock.test.mjs`: the measured-part unit test is stricter (a value only in a page's
  request body is refused, a write needs a step-caused time) and a unit test of `causedWrites`.
- Mutations M54-M62 (9 new, 62 in all), each caught by `test/measured-part.test.mjs`; M47 and M50
  follow the lines they mutate, which this round rewrote.

### A decoy beside the page's own write

One step-caused declared write is not enough on its own: a step could cause a decoy (an approval of
another record) while the page sends the real one from a timer. So every declared write the page
sent during the measured part must be the person's: caused by a step, or the next part of one that
was (a write of the same kind that went out within the reaction time of the previous one's answer:
Odoo's import sends its rows in batches, each once the last is answered). Plant R2d, mutation M62.
On the real drivers, every declared write of both products in this round was caused by its step,
bar the import's later batches, which continue the first.

## Cost

Measured on this machine (user plus system time of the process and its children, `/usr/bin/time`),
with other agents' verifies running beside it:

- `test/measured-part.test.mjs` (13 tests, 12 with a browser): 19 s (13.2 user, 6.0 system).
- The nine new instrument mutations (M54-M62, each running only the self-tests named for it, one
  control for the file): 30 s (21.4 user, 8.5 system).
- The two new ours drivers in the verify health check: a few seconds each.
- The whole comparison suite: 286 s (390 tests; 230 s for 374 in round 10); all 62 mutations:
  156 s.

So the round adds about one minute of processor time to `./erp verify`, inside the owner's 10,500 s
maximum (`verify.cpuSeconds`, needs-human #12, unchanged; this round changes no maximum).
