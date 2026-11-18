# p09 custom fields, round 3: gate processor time (integration fix 1)

Date: 2026-11-16. The integrator's alone run of the round-3 merge passed every test but used
22,651 CPU s (dotnet 19,001, web 2,796, e2e 754, timing 101) against a re-integration limit of
21,300. The same machine's alone run of the base (p12-jobs r6, 018c0aba) used 19,902 s.

## Finding: the round adds no request and no code path to the shared gates

- The gates (G1HttpIsolationTests, G1NonInterferenceTests, G1ListVolumeTests, G2) enumerate the
  registered list columns (`list.Columns.Where(c => c.Groupable)` in ListAnswers.cs, G1HttpIsolationTests.cs,
  NonInterference.cs, ListVolume.cs, G2PermissionTests.cs). A custom column is added per workspace at
  request time, by `ListCustomFields.Column`; no registered column carries one and no gate tenant is
  seeded with a custom field. So making custom columns groupable (`Groupable: Type != Lookup`) adds no gate
  request.
- Date buckets (`cfX.month`, `cfX.year`) are parsed only when the name has a dot and names a custom
  date column of the list; the descending-blanks-last order and the cursor change apply only to custom
  columns (`IsCustom`, a scan of an empty list for every other column). For every registered column the
  generated SQL is identical to the base's.
- The migration adds one SQL function (`customfields.value_date_trunc`) to each database a test creates.
- The slowest tests of the run are the shared gates (G1HttpIsolationTests 10,894 s, G1NonInterferenceTests
  3,949 s, G1ListVolumeTests 1,935 s); the classes of this piece are 7 to 11 s each (trx of the kept folder).
- The integration branch without p09 already runs at this level on this machine: p07-audit r5's alone runs
  of the same day measured dotnet 18,199 s and 19,067 s (kept folders 20261115T092738Z-155902 and
  20261115T195721Z-3344) against 16,783 s for p12-jobs r6. The +2,749 s of the p09 run is therefore not
  shown to come from p09's code; the gate processor time of a quiet alone run varies by more than 2,000 s
  between runs of the same code (16,077 to 19,067 s dotnet over 11-14 November runs).

## Decision

No gate was narrowed and no code changed to chase the number: there is no p09 request, column or
code path to cut, and the shared gate machinery is p00's. This round's processor time added by the
piece's own tests: CustomFields.Tests project 28 tests, 37 CPU s; Rules namespace 54 tests, 6.6 CPU s.

## Targeted run (this session, host, Ryuk disabled, nothing else of mine running)

- Erp.Modules.CustomFields.Tests: 28 passed.
- Erp.Gates.Tests, namespace Rules plus G1TenantSourceTests: 54 passed.
- The merged ratchet.json keeps the integration branch's minimums (dotnet 1078, web 747, e2e 152).

For the integrator: the limit decision belongs to the owner (needs-human #34). The run is 1,351 s
over the re-integration limit with every test passing; re-run when the machine is quiet (the new 96 GB
RAM, wall 12,980 s vs 18,495 s) or ask the owner to widen the allowance.

## Re-integration 3 (serial-22): third alone run over the limit

The serial-22 alone run (merge 5fb842e2) passed every test (dotnet 1141, web 749, e2e 158, compare 489, build 13) and used 26,059 s against 25,300 s (decision #54 limit). Dotnet was 22,184 s, about 3,700 s above the previous p09 run. It was the first alone run under the default of 3 concurrent dotnet test groups (11589157), which p00 r20 measured as raising counted processor time. p09 changed no product code, test or gate in this round; its own classes take 7 to 11 s each and add no gate request (no gate tenant has a custom field). Fixer re-merged the integration branch (c4321812): CustomFields.Tests 28 passed; Gates Rules namespace plus G1TenantSourceTests 54 passed. No full verify run by the fixer. The remaining cost is in p00's parallel-group counting, outside this piece.
