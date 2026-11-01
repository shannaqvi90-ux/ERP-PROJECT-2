# p12 jobs: see a job and run it again, ours against Odoo

Piece p12 (round 1). Task: `gauntlet/compare/tasks/see-and-rerun-job.mjs`. Status: driver written, not yet judged.

## What Odoo needs (its baseline driver)

Settings, scroll, activate the developer mode, Technical, Scheduled Actions, search the job, open it,
Run Manually. Odoo shows the next execution only: the last run is nowhere in its interface and a manual
run says nothing when it ends.

## Our path (`gauntlet/compare/drivers/ours/see-and-rerun-job.mjs`)

1. `g` then `j` (go to Background jobs; the search box has the focus when the list opens).
2. Type the job's name.
3. `Enter` opens the best match (equal names rank by newest first, so the job that ran last).
   The panel shows when it finished, its tries, parameters, result and output.
4. `Alt+R` runs it again. The new run opens in the panel, goes from Waiting to Running to Done, and
   the panel shows when it finished; the list refreshes with it.

No click, no developer mode, no scrolling; the last run is on screen before the run is started and the
end of the new run is announced. Odoo's input names a scheduled action ("Mail: Email Queue Manager"); ours
runs the job it finds by the name of its kind (the self-check), which is the nearest thing of ours (a job
of a kind with a run permission that people may repeat). The driver's verify checks that a job with
`source = rerun`, `rerun_of` = the job that was opened, finished after the task started.

## Choices that make ours fast

- The worker polls every second in the demo stack (`ERP_JOBS_POLL_SECONDS`, compose) and the seeded
  self-check history is a one-step run (parameters `{"steps": 1, "pauseMilliseconds": 0}`), so a run
  again finishes in about two seconds. The panel reads an open job every 1.5 s while it is queued or running.
- Re-running opens the new run at once (the address moves to it), so the person never searches again.

## Not tried

A "run now" on a schedule (Odoo's scheduled actions are closest to our schedules). Ours has the schedule
(name, timing, last and next run) and the jobs it queued, and Run again on any of those jobs; a schedule
button would only repeat that. If a critic finds the task is judged on the schedule, it is a small addition
(`POST /api/jobs/schedules/{id}/run`, `jobs.jobs.run` plus the kind's own check).
