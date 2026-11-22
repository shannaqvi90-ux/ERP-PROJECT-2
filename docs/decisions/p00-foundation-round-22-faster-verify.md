# p00 round 22: a faster verify with exactly the same tests (owner decision #59)

Date: 2026-11-21. Piece: p00-foundation. Status: accepted. Routed by the lead under owner decision #59 ("chaaron pe
kaam karo, takay hamare kaam ki speed tez ho, aur output ki quality same rahay"): the same tests, gates, attacks,
controls and ratchet minimums, in less time. No timeout was raised, no test skipped, narrowed or deleted, no ratchet
maximum touched (the dotnet test count minimum rises with the tests added below).

## 1. Test PostgreSQL servers in memory

`TestDatabaseServer` starts each test process's Testcontainers server with its data directory on tmpfs
(`WithTmpfsMount("/var/lib/postgresql/data")`), on top of the settings it already had (`fsync`,
`synchronous_commit` and `full_page_writes` off). Reason: the servers are thrown away with the process, the gate
databases are written and copied (`CREATE DATABASE ... TEMPLATE`) constantly, and the disk behind Docker Desktop
on WSL2 is the slowest part of that. Scope: only the servers the test library starts. The product's own settings,
the verify stack's demo database and the `compose.yaml` database are untouched. Cost: the databases of one process
now count as memory (a gate environment is a few hundred MB, at most a few GB for the volume tests); a data directory
is gone when the container is, as before.

## 2. G1 non-interference check in three shares, judged together

Before: one test (`G1NonInterferenceTests`), 55 to 80 minutes, the longest single test after the HTTP shares.
Now (same pattern as the HTTP attack of round 20):

* `NonInterference.RunAsync(env, only, shardIndex, shardCount)`: in every share both tenants still make every write the
  app offers first (the state every comparison starts from is the whole check's); then only the read endpoints and
  the compared writes the share owns are compared. `NonInterference.ShareOf` deals endpoint families
  (`AttackShard.FamilyOf`, so the write pairs of one family stay together) to the share with the least weight
  (`ShareWeight`: a compared write 450, a read 25 or 60, a create or delete 5).
* `G1NonInterferenceShardTests` (trait `NiShard` = 0, 1, 2), each in an environment and a test process of its own
  under `./erp verify` (groups `ni-0` to `ni-2`), each leaving its result in `$ERP_GATE_SHARD_DIR`
  (`g1-noninterference-shard-<i>-of-3.json`). A share fails on its own findings and blind spots.
* `G1NonInterferenceTests.What_tenant_A_is_answered_never_depends_on_what_tenant_B_did` (same name, trait
  `Judge=noninterference`, group `ni-judge` after the shares) is the verdict: `NonInterferenceJudge.Failures` refuses
  a missing or repeated share, shares that disagree on the inventory, a read or write endpoint compared by two shares
  or by none, a share that compared an endpoint it does not own, any finding or blind spot in any share, the
  counts that are only meaningful over the whole check (comparisons, writes, Arabic answers, headers, discriminating
  answers: each above zero over the shares added up), and every ratchet minimum the single check had
  (`g1.nonInterference*`, `g1.writeNonInterference*`) against the shares' numbers added up. A plain `dotnet test` (no
  shard directory) runs the shares inside the judge, as the HTTP judge does.
* `build/verify-inside.sh`: groups `ni-0..2` replace `noninterference`; `ni-judge` runs with `http-judge`; the partition
  check lists the shares and the judge, and the `gates-rest` filter excludes `G1NonInterference` (both classes), so every
  test is still in exactly one group.
* Self-tests (`NonInterferenceSelfTests`): the planted-module test now runs the planted surface as two shares in two
  environments and takes its verdict from the judge: both shares must find planted interference, the judge must name
  both, the merged findings must still carry every assertion the single run carried, and no partition failure may show;
  `The_judge_of_the_shares_refuses_a_set_that_is_not_the_whole_check` plants a missing share, a repeated share, an
  endpoint in two shares, an endpoint in none, different inventories, an interference found in one share only and a
  count below its minimum, and checks that `ShareOf` is disjoint and whole for 2, 3 and 4 shares.

Measured (the three shares side by side through the slot script, load 8 to 15, tmpfs on): wall 24:01, 23:20 and
21:31 minutes; processor time (user + system of `dotnet test` and its children) 695 s, 675 s and 618 s, 1,988 s
together, against 2,184 s for the one group in the round-21 critic's run. The judge passes against every ratchet
minimum. The wall time of the group drops from 55 to 80 minutes to about 24; the work repeated in each share is the
tenants' writing (small next to the comparisons).

## 3. One template per run

Already so within a test process: `TestDatabaseServer.GateTemplateAsync` seeds one template database per process and
settings combination and every gate environment is a `CREATE DATABASE ... TEMPLATE` copy of it; explicit-plan
environments (`StartAsync(SeedPlan...)`) are seeded where they are used because their plans differ (the volume tests'
sizes, canaries made per call), so sharing would change what a test sees. Across the test processes of one verify a
shared template would need one shared server for all of them (the roles are server-wide, with passwords chosen per
process, and their bootstrap takes turns); that is a larger change than this round and was not made. What it would save
is a seeding of the gate template per process (about a minute each), a few minutes of processor time over the run.

## 4. G2CompanyRoleTests.A_role_held_in_company_X_acts_on_nothing_of_company_Y

Cause (reproduced: it failed in the class run and passed alone): the class's other tests share one database and switch
users between the companies, which leaves audit entries about rows that are company Y's now and were company X's when
written; the administrator of X may read those (`GET /api/audit/log`, `/api/reports/lists/audit.log`), and the
attack's snapshot of company Y's row ids then found a Y id in an answer. It was not another test class (each class has
its own database). Fix, both ways asked for:

* the attack runs in a database of its own (a copy of the gate template), so no other test writes into it;
* an id found in an answer is judged against what is company Y's at that moment (`CompanyRoleRecords.IsStillCompanyYsAsync`):
  excused only when the row is in another company now; company Y's own id, a row still in Y, a text of Y and an id
  found in no company table stay markers. `A_planted_company_Y_leak_is_still_a_leak_and_only_a_row_moved_out_of_Y_is_excused`
  is the self-test, and the existing planted-module test (`The_company_role_attack_catches_...`) still catches the
  planted text and branch leaks. A leak message can carry the answer around the marker when `ERP_GATE_DIAG=1`.

## Gate backlog rows

* Row 17 (p00): `ReadPermissionWrites.Check` now refuses an endpoint that changes data under a read permission that
  is reviewed in `read-permission-writes.txt` and also listed in `write-set-unexercised.txt` (a reader's write no gate
  sends), and every unexercised endpoint must name in its reason an existing test file that sends it
  (self-test `A_reader_write_no_gate_sends_and_an_unreached_endpoint_without_its_own_test_are_refused`).
* Rows 3, 13 and 15 (p00): the Host-header variants (`G2PathCaseTests.Hosts`), the varied-case paths and the
  trailing-slash paths are in the G2 matrices since rounds 15 to 18; no new code this round, the lead can close them.

## Not done

The full `./erp verify` before and after was not run by the builder (owner decision #41: the integrator's alone run is
the first full verify). The integrator's alone run gives the stage numbers; the group files `cpu-group-ni-*`
replace `cpu-group-noninterference`.
