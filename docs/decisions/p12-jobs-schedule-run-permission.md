# p12-jobs: a schedule needs the kind's run permission (round 3)

Decision: a schedule runs its kind as system work, so adding one (POST /api/jobs/schedules) is refused with 403
`jobs.typeNotAllowed` (English and Arabic text already in the Jobs resources) when the caller lacks the kind's
`RunPermission`. Changing one (PUT) is refused the same way when the change switches the schedule on, changes its timing
(cron) or changes its parameters; switching off and renaming need only jobs.schedules.update. `GET /api/jobs/types`
reports `schedulable` true only when the caller also holds the kind's run permission (same test as Start and Rerun).

Gate: `tests/Erp.Gates.Tests/G2/JobsWriteSetTests.cs` gives a user exactly jobs.schedules.create (and another exactly
jobs.schedules.update) and requires 403 for every schedulable kind (a schedulable kind missing from the gate's table fails
it), and asserts the per-kind run permission on Start and Rerun. Closes gate-backlog row 12 (plant P2: the Rerun check removed is now caught).

Processor time added: one test class method pair on the shared Gates environment, a few seconds (about 10 s of processor time);
no new endpoints, so no per-endpoint gate cost.

Round 3 refinement: a schedule added switched off (isEnabled false) runs nothing, so adding it needs only jobs.schedules.create;
adding one switched on needs the kind's run permission, and so does switching one on later (PUT). No run permission was changed
or widened: the clean-up kind still needs jobs.schedules.update and the self-check kind jobs.jobs.run, and deleting history by
hand stays with jobs.schedules.update holders. The generic one-permission write-set gate reaches POST /api/jobs/schedules
through its "isEnabled turned" request (a disabled schedule) and nothing is added to write-set-unexercised.txt;
JobsWriteSetTests sends the enabled requests with and without the run permission, and judges the audited writes (only the
schedule row). Known consequence: someone with schedules.update and the run permission who switches on a schedule
another person added switched off takes on the parameters it holds; the form shows them before the switch.

Small fixes of the round-2 critic's list: the Arabic jobs list shows titleAr; the Schedules list shows the kind's name (the key only
for a kind this build no longer registers); the job form's note beside Run again and Cancel says jobs are not edited instead of
the generic read-only text; the seeded clean-up schedule is named in one language (Arabic when the first administrator works in
Arabic); Ctrl+Enter starts the job from the Start dialog; the comparison driver waits for the list to be filtered to the typed
name at the bar volume before pressing Enter.

Processor time added: the new gate method pair and the web tests are a few seconds (about 15 s of processor time); no new endpoints.
Closes gate-backlog row 12 (plant P2: the Rerun check removed is caught by Starting_and_running_again_each_need_the_run_permission_of_the_kind).
