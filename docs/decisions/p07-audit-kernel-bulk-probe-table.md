# p07-audit: the kernel bulk-insert test writes a probe table, not audit.entries

Date: 2026-11-17 (round 6)

## Context
Round 5's tamper-evidence migration makes `audit.capture()` the only writer of `audit.entries`; the
application role has no INSERT there. `KernelDataTests.Bulk_insert_loads_100000_rows_through_row_level_security_quickly`
and `Bulk_insert_cannot_smuggle_rows_into_another_tenant` used `audit.entries` as the target of
`BulkInsert.InsertAsync`, so the first failed in the timing stage with 42501 (the second "passed" only
because it expects 42501, for the wrong reason).

## Decision
Both tests now write to `kernel_probe.bulk_rows`, a tenant-owned table the test creates through the admin
connection with the same columns and the standard `tenant_isolation` policy that `ProtectTenantTable` gives
a module table: row-level security enabled and FORCED, DML granted to the application role. No audit trigger
(the measured thing is the bulk path through RLS, as before).

The purpose is unchanged: 100,000 rows loaded as the app session through row-level security in under 30 s,
and a row for another tenant refused with 42501. Row count and budget are not touched. The smuggle test now
fails only for the reason it names (the RLS WITH CHECK), not for a missing grant.

No sanctioned bulk path into `audit.entries` was added: tamper evidence says nothing but the trigger writes it,
and seeding of audit rows goes through audit.capture() on the audited tables.

## Cost
No product code changed this round. The test adds one CREATE TABLE in the kernel test database. Processor
time of p07's own work (item 2 of the round) was not reduced in this round: see the notes to the integrator.

## Integration fix 1 of round 6 (processor time)

The integrator's alone verify of the merge (45c217e9, log verify-integrator-p07-audit-r6.log, kept folder
.verify-failed/20261117T141856Z-574843) passed every test (dotnet 1147, web 752, e2e 157, compare 489, build 13,
timing 7, mutations 134/134). The only ratchet failure was verify.cpuSeconds: 24,580 s (dotnet 21,239, web 2,415,
e2e 829, timing 96) against 23,300; the re-integration allowance ends at 23,800, so the run was 780 s over it.
Load 0.75 at the start, peak 29.02 on 16 CPUs.

Looked at, from that run's trx files and shard counters: the dotnet stage's time is the G1 gates (non-interference
3,772 s of test time, the four HTTP shards 1,662 to 2,883 s each, list volume 1,749 s, GateSelfTests 1,057 s); the
audit log is a share of their list queries (its `fields` choice has 165 values, each judged by eq and ne and a sorted
twin), but per request it is cheap in the .NET process (the database containers are not counted) and no per-request
saving was found in the audit module. The only large saving in p07's own work is fewer `fields` choices or a text
filter in place of the choice; that changes the contract the module tests pin (`fields eq 'name_en'`, `ne`, 400 for a
field recorded nowhere), so it was not done (never narrow a test). The dotnet stage's processor time for near-identical
trees has varied by about 3,000 s between alone runs, so a saving of 800 s cannot be shown by one run either.
No code changed in this fix; no verify was run (the integrator's alone run decides). Needs the owner: a higher limit
for this re-integration, or a decision on the `fields` filter contract.
