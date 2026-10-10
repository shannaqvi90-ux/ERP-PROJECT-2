# p01: a task that saves is saved by the measured part, in its declared end state (instrument 8)

Round 10 of p01 (Odoo reference rig and blind comparison harness). Answers the round 9 critic's
biggest gap and its other instrument findings.

## The gap

Round 9 made `verify()` blind to which call it is in and required, for a task that saves, that one
of `verify()`'s back-end reads answer differently after the clock than before it. Two routes were
left (critic p01 r9):

- **Q1.** A task that names nothing the person enters (an approval, a rerun, a language) accepted
  *any* read that changed as the saved state. Set-up did the task; `run()` made one cheap,
  unrelated change (a version, a preference); a `verify()` that gated on that change answered
  "not done" before the clock and "done" after it. On the real api-update-user driver a lost task
  (2/249 against 2/184) was recorded as a win on every metric (1/84 against 2/184).
- **Q2.** The runner closed set-up's browser contexts without waiting for requests already sent.
  A slow save set-up's page started landed on the product after the clock started, and a measured
  part of one key verified.
- Two defences no self-test pinned: set-up's local storage kept out of a signed-in start (X12) and
  the clipboard emptied at the start (X16).

## Decision

The harness, not the driver, decides what counts as the saved state, and the measured part must be
what saved it.

1. **The task declares its end state** (`endState` in the task file, per product whose driver is
   built): the back-end reads that hold it (`METHOD /path`, `*` for one segment) with the parts of
   their answers that hold it (keys from the top, list positions left out, a part naming its
   subtree), and the writes that save it. Task files are reviewed with the product-neutral task, so
   a planted driver cannot move the end state to a read it controls. `test/tasks.test.mjs` requires
   a declaration for every built driver of a task that saves (19 today) and checks its form. The
   declarations were checked against every recorded run of round 9 (Odoo baselines and ours
   comparison runs): each honest run changes a declared part.
2. **Only a change there counts.** `savedState` keeps the round 9 rule and adds: a changed read
   that is a declared read, in a declared part; for a task that names what the person enters, the
   entered value must arrive in those parts. A version, a preference, another field of the same
   record proves nothing (Q1, Q1b, and the real-driver plant are self-tests).
3. **The measured part sent the write.** At least one request of the measured part writes to the
   product (not GET, HEAD or OPTIONS and not one of the reference's documented reads); when the task
   declares its writes, one of those. Every entered value the end state gained must have been entered
   by the measured part: typed (in pieces too), picked as a file, or sent in one of its writes or
   API requests. This catches a save that arrives without the person sending it (Q2, a job set-up
   scheduled: Q3, Q3b, Q3c).
4. **Set-up's browser writes are answered before the start.** Every browser context of the run is
   watched from its first request (the runner patches the run's own `browser.newContext`, which
   `newPage` also goes through, so contexts a driver opens are watched too). Before set-up's
   contexts close, the runner waits for their writes to be answered, so a save lands before the
   clock and "already done before the clock" sees it. A write the browser abandoned (its page moved
   on or closed, the request failed) may still be carried out by the product: the run is refused.
   A product keeps working on a request whose client has gone, so "the browser gave up" is not "the
   product did nothing".
5. **The clipboard is read back** after it is emptied (pasted into a scratch page that closes
   again); text set-up copied must be gone. X12 and X16 are self-tests now.

Instrument version 8: every Odoo baseline was captured again (`node run.mjs --task all --product
odoo --repeat 3` on the shared rig), and each saves baseline records `saved_state.end_state_changed`
and `writes_sent`.

## Alternatives not taken

- *A driver declares its end-state read.* The driver is what a plant changes; the declaration must
  live where the plant cannot reach it without a reviewed change to the task.
- *Require every task to name an `enters` value.* An approval, a rerun or a language switch has no
  value the person types; a declared read and part says the same thing for every task.
- *Wait for abandoned requests a while.* The browser reports no end for a request abandoned by a
  navigation; any wait would be a guess at how slow the product is.

## Limits

A task that declares no writes accepts any write of the measured part; a job set-up scheduled on the
product that lands while the measured part sends the declared write is not told apart from it.
Tasks and drivers stay reviewed code.

## Cost

PROCESSOR_TIME_NOTE
