# p12 jobs round 6: labels for recorded choices, schedule parameters, seeded best match

## Choice labels (audit.choice.*)

The job panel's change line and History tab print recorded values through `audit.choice.<schema.table>.<field>.<value>`.
p12 registers `jobs.jobs.status`, `jobs.jobs.source` and `jobs.job_attempts.outcome` in `web/src/modules/audit/i18n/{en,ar}.json`
(the audit module's server embeds only those files, so the registry for the printed log is the same file; numbering did the same).
The words are those of the jobs list (jobs.status.*, jobs.source.*, jobs.outcome.*). The schedules table has no choice column (its only
flag, is_enabled, prints Yes or No). Gate: `AuditGateTests.Every_recorded_choice_value_of_the_jobs_tables_has_a_label_in_both_languages`
reads every CHECK of the jobs schema that lists values and requires a label in both languages (Arabic with Arabic letters).

## Schedule update: parameters compared by meaning

The request's parameters were compared as text with the stored jsonb text (which PostgreSQL reorders and spaces), so any save that sent
parameters counted as changing what runs and a user without the kind's run permission could not switch off or rename. Now `SameJson`
compares objects by members, arrays by order and numbers by value. G2 gate: `JobsWriteSetTests` switches off and renames a schedule with
nested parameters sent back unchanged.

## Omitted isEnabled

`POST /api/jobs/schedules` already treated an omitted or null isEnabled as true for the run-permission check; the critic's planted
fault (P6) showed no gate sent it omitted. `JobsWriteSetTests` now sends it omitted and null (403 jobs.typeNotAllowed).

## erp_worker columns

Its policies are USING (true), so the column grants are the whole protection. `JobRunnerTests.The_worker_lookup_role_may_read_only_the_columns_its_lookup_needs_and_write_nothing`
compares `has_column_privilege` over every column of every schema with exactly the seven reviewed columns and requires no write privilege.

## Best match at bar volume (comparison task see-and-rerun-job)

The seeder made three waiting self-checks just before the last three rows and relied on one of those being a self-check; at 100,000 jobs
usually none was, so Enter opened a waiting job that cannot be run again. The very last row (count > 20) is now a finished self-check at
every volume. The test no longer depends on test order: it checks the seeded history alone (jobs with no creator) and then runs a fresh
self-check and requires it to be the best match. Failed jobs now carry their tries at every volume (about 8% of jobs, 2 to 3 tries each).
The path stays Enter-opens-best-match; the task is a win with margin (round 5: ours 2.4 s against Odoo 4.9 s), so no tie reasoning is needed.
Processor time added by the round: the new tests add under 5 s; the seeder adds about 20,000 attempt rows to the demo seed.
