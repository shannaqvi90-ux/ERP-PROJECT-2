# p01: timing that lives outside the page's thread causes nothing; a fixed prefix a field shows

Round 13, answering critic p01 r12 (BLOCKED: plants W1 and W3 recorded as verified with one step and
a win against Odoo's follow-approval baseline; W2 refused only by accident; mutations K6, K8, K9
missed) and the lead's decision on critic p06 r7's question. Instrument version 11.

## Problem

Round 12 traced each write back through Chromium's record of the page's tasks. A task "of no
traceable origin" (a message) counted as the step's when the page task just before it was caused and
no uncaused task of the page's own thread (or of a dedicated worker) ran in between. Timing that lives
outside that thread was invisible to the guard:

- W1: the stand-in's server pushed "show" over a web socket the page opened at load (a Close button
  appeared), then "go" 500 ms later on its own timer. One click on Close sat between the two pushes,
  so the "go" message, and the approval it sent, were credited to the click.
- W2: the same through a cross-site frame's `postMessage`. It was refused only because the harness
  could not arm its read world in a page with a cross-site frame (`SecurityError`): such a product
  could not be measured at all.
- W3: a click on plain text asked something held 1.9 s (under `LINK_SECONDS`), then a 0.9 s timer
  (under `REACTION_SECONDS`) approved: each link stayed under its limit while the chain held the
  write back 2.8 s, which S10 refuses at 2.5 s.
- K6 (a browser key counted no keystroke), K8 (human plus wait without the wait) and K9 (fixed
  review letters) passed the whole suite.

Looking for more of the same family, I found three more that round 12 credited (each verified with
one click, checked against the round 12 instrument): W5, a CSS animation the page started on its own
whose `animationend` approved just after a click; W6, a resize observer that approved when such an
animation reached its end; W6b, W6 with an animation started before the clock that runs past it.
Two more were refused by round 12 only by accident (a chain "too long to follow": the task that
commits a document holds its own request and its answer, and the walk looped): W7, a document the page
asked for before the clock and the server held until just after the click, whose script approved;
W8, a meta refresh the page set that loaded an approving page just after the click.

## Decision

1. **Messages are traced to where they came from** (`lib/causation.mjs messageCause`):
   - a **web socket's message** (`WebSocketReceive`) is the answer to the page's last frame on that
     socket (`WebSocketSend`, same frame and socket id) when a caused task sent that frame within
     `LINK_SECONDS` and the server had not answered it already; otherwise the server pushed it when
     it chose and it causes nothing (W1);
   - a **window's postMessage** (`HandlePostMessage`) is the task's that posted it (`SchedulePostMessage`,
     same `traceId`); with no posting task in the record it came from another process (W2);
   - a task started by a mojo message on an interface that carries **another frame's, tab's, worker's
     or server's** message (`OUTSIDE_CARRIERS`: web sockets, web transport, broadcast channels, a
     frame's messages, service and shared workers, peer connections, push, storage events) causes
     nothing;
   - a task of no traceable origin (a message port, a worker's message) is refused while **any other
     thread of the page's script** ran from the step's input on: a worker of any kind or another
     frame's or window's renderer main thread (the trace holds every process of the browser; W4: a
     shared worker's port). A task that delivers what the browser read for the page itself (a file or
     blob read, a response's body, the device's answer: `IN_PAGE_DATA`) cannot come from another
     thread and is not refused for one: Odoo keeps a shared worker (its bus) busy all the time, and
     the first recapture of this round refused one of three honest attach-file runs (its upload
     follows the file's read, `blink.mojom.BlobReaderClient`) for that worker's activity;
   - a **document** is its navigation's: the task that commits it is judged by the task that started
     the navigation (`FrameLoader::StartNavigation` with the same address): a click on a link is the
     click's; a navigation started before the clock (W7) or by a refresh the page set
     (`HttpRefreshScheduler::NavigateTask`, W8) causes nothing.
2. **The chain's delays add up**: a write must go out within `LINK_SECONDS` (2 s) of the input (or
   command) of the step that began its chain (W3: 2.8 s).
3. **Time-driven script**: an event a running animation, transition or media element fires
   (`animationend`, `transitionend`, `timeupdate` ...) causes nothing (W5); an idle callback is its
   asker's (`RequestIdleCallback`/`FireIdleCallback`); script the browser runs for an observer as it
   renders a frame causes nothing while an animation or transition of the page runs (Chromium's
   `Animation` begin/end events, and `document.getAnimations()` read in the read world as the clock
   starts, `animations_at_start`; a page that cannot be read counts as animated; W6, W6b).
4. **A page with a cross-site frame can be measured**: the read world skips a child frame's window
   when it arms (page functions never reach one: `window`, `frames`, `contentWindow` are refused).
5. **Self-tests for the missed mutations**: a browser key counts its keys (F5 1, Alt+ArrowLeft 2);
   human plus wait equals the human time plus every system wait; the review page draws letters at
   random per task and shows each product under the letter key.json gives it (40 tasks, both orders).
6. **A counted triple click** (critic p06 r7, routed to p01): one step, P + 6B; typing right after it
   into the field continues it. The Odoo edit-and-save driver uses it (Ctrl+A cost a step).
7. **A fixed prefix a field shows** (lead decision on critic p06 r7's question): a value may begin with
   a prefix the field shows as its own, which need not be typed, only when the task declares it for
   that product's field (`prefixes`) as an international dialling code that a **constant of the
   product's own code** sets (`source`, `constant`: `test/tasks.test.mjs` reads the file and checks the
   value, so a prefix set-up, a tenant's setting, the data or the task seeded is not one); the field
   shows it in the start screen before the clock (`prefixes_at_start`); the rest of the value is typed
   in full; and the end state holds the whole value with the prefix as written (its `+`). The same
   rule applies to Odoo: a prefix its field showed would be declared the same way, after a check
   against the running rig (its code is not in this repository); Odoo's phone field shows none.
   Plants PF1 (set-up seeds "+971 50 555", the person types "0199"), PF2 (an undeclared prefix), PF3
   (an empty field that offers +971 only once focused, after the clock started), PF4 (a back end that
   drops the +), and declaration tests (a longer "+97150", a constant whose value differs, no source,
   a path outside the repository, a prefix of the reference).
8. **edit-and-save drivers**. Odoo: triple-click the phone field, type the digits only
   (971505550199, which the instrument credits: separators free; typing only the 9 digits after +971
   stores 505550199, not the number), Save; `verify()` compares letters and digits (separators
   free), not the exact spaced text. Ours: a `national` path that types the 9 digits after the +971
   the field keeps (the product's `HOME_PREFIX` in `web/src/kernel/forms/phone.ts`), now its `run`.

9. **A callback is matched with the last request before it.** Timer, animation-frame and idle ids
   start again in each new document of a frame (Odoo reloads its page to turn on developer mode). The
   record kept only the last request of each id, so a callback before a reload could be chained to a
   request made after it: in a configure-sequence run of this round, Settings' first `base_setup/data`
   (0.35 s into the clock) was credited to step 7 (1.39 s), a chain running backwards in time. Each
   request is now kept and the last one before the callback is used; a chain that begins after the
   write it explains is refused. Each caused write now records its chain's length in seconds
   (`browser_record.chain_seconds`).

10. **The harness's reads are not a step's doing.** A DevTools command inside a step's window
   counted as that step's. That is right for a step whose action is a command (a file chosen, the
   device's answer, a scroll into view), but during a click the harness also reads the page (its
   actionability checks, its wait for a navigation), and the fourth verify of this round showed a
   document that arrived during a click credited through such a read ("a DevTools command of step 1 >
   a task that followed at once"). During a click, a key or typing the harness's commands now cause
   nothing and are passed over when the page task before an untraceable one is looked for; every
   DevTools-rooted chain of the 24 Odoo baselines is a file pick or a device step, so none changes.

Mutations M86-M112 (one per rule above), each caught by its self-test.

## Alternatives considered

- **Treat every untraceable task as uncaused while a long-lived channel is open** (the critic's
  option a, second form). Odoo keeps a web socket open in a shared worker the whole time, and its
  form's saves follow body reads of no traceable origin; every Odoo save would have been refused.
  Tracing the socket's own frames refuses the push and keeps the request-and-answer shape.
- **Chromium's task flow events** (`toplevel.flow`) to link every posted task to its poster: they do
  not cross a mojo pipe to the sending task (a port message is handed over by the pipe's watcher),
  and they multiply the record's size. The mojo interface name of the message that started a task
  (`Receive mojo message`, `SimpleWatcher::OnHandleReady`) names where it came from at no extra cost.
- **Bound each link more tightly** (answers at 1 s): round 11's honest control waits 1.3 s for its
  check; bounding the sum keeps every honest chain and refuses W3. Measured this round: in the 24
  re-captured Odoo baselines (69 caused writes recorded) every chain took 0.002-0.495 s from the
  step's input to the write.

## Ties

No task this piece owns ties Odoo. The instrument change moved Odoo's edit-and-save to 3 steps and
12 keys (triple click, digits, Save); ours' `national` path is 2 steps and 9 keys.

## What it still cannot tell

A product that does the task on any click (set-up turned on such a setting), including a click that
asks over a web socket and acts on the server's answer, shows a click that caused the write; the blind
screenshots and review judge that, as before. A message port to another process is refused only when
that process ran script in the window the guard watches; one whose sender ran earlier and whose
message the page's busy thread held back is refused by the busy-input guard.

A read the browser does for the page (a file chosen, a body) is traced by the task just before its
result, as in round 12: Odoo's attach-file upload follows its file's read, and when one of Odoo's own
timers fires between the file pick and the read's end the run is refused (one run in three in both
recaptures of this round), never credited. Telling which task started a read would need an event
Chromium does not record; a refusal of an honest run is the safe side (the baseline is the median of
its verified runs).

## Cost

No new dependency. The record is taken only while a task that saves is measured, as before; the
start reads one value from the page (running animations, and a declared prefix's field). This round
adds 18 end-to-end plants and controls to the comparison suite (W1-W8, PF1-PF4, five controls), each a
browser run of a few seconds, and 25 mutations (21 judged on synthetic traces, 4 on browser runs).

Verifies of this round (`./erp verify` through the verify slot, busy machine, load 16-65):
- run 1 (slot taken 2026-10-18T17:21:44Z): every stage's tests passed except one missed mutation (M66,
  masked by the chain's bound; its self-test now holds a 1.5 s timer). Processor time: dotnet 7,315,
  web 2,044, e2e 401 s.
- run 2 (19:54:50Z, after merging the integration branch with p00's and p06's rounds): every test
  passed (dotnet 697, web unit 522, e2e 111, compare 446, build 6; 110 of 110 mutations caught; every
  built ours driver verified, edit-and-save 2 steps and 9 keys), but the processor time was 10,868 s
  against the 10,500 s maximum: dotnet 8,184 (7,315 in run 1, a stage this piece does not touch),
  web 2,158, e2e 476, timing 49.
- run 3 (22:25:38Z): plant W1 met two Close buttons on a loaded machine (the stand-in's own "go" came
  before the click) and ended in an error rather than a refusal; the plants now click the first and
  check the record's reason only when the approval went out inside the clock. dotnet 7,783 s.
- run 4 (2026-10-19T00:39:11Z): the link control's chain was rooted in the harness's read during the
  click (decision 10 above). dotnet 7,331, web 1,707 (stopped at the unit tests), e2e 407 s; every
  built ours driver verified.
The web stage, which holds the comparison harness, used 2,044-2,158 s against round 12's 1,922 s:
this round adds about 120-240 s of processor time.
