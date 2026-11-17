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
