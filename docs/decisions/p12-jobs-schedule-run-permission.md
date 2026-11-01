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

Left for later (not done this round to keep the re-integration cheap): Arabic job title (titleAr) on the Arabic list;
kind labels instead of raw keys on the Schedules list and for unregistered seeded kinds; the "Read only" note beside working
Run again and Cancel; the seeded schedule name holding both languages in one field; Ctrl+Enter in the Start dialog;
Enter at the bar volume in the comparison driver.

Follow-up in the same round: the generic G2 gates (G2SubjectTests, G2WriteSetTests) call each endpoint as a user holding exactly one
permission plus the read permissions, and could no longer create a schedule once the check existed (the self-check kind needs
jobs.jobs.run). The clean-up kind's run permission is now jobs.schedules.create (it was jobs.schedules.update) and the create
request's API example names it, so a create-only user can schedule and start the clean-up, while a user without jobs.jobs.run cannot
schedule the self-check. Cost: someone holding only jobs.schedules.update cannot switch a clean-up schedule on or retime it.
