# p02 — The company and branch attacks also read with the attacker's own parameters

Date: 2026-10-08. Piece: p02-tenancy. Status: accepted (round 7).

## Context

The round 6 critic planted three one-line faults, each reading branches past tenancy's branch filter
(`IgnoreQueryFilters([BranchFilterName])`): the company profile report (B3), the branch directory
report (B3b) and the branch options of the access screen (B1). Each was a real leak on a running
build: a user limited to one warehouse printed every branch of the company, with phones. Every G1
and G2 gate passed. The branch attack only ever sent branch Z's own ids and texts into routes,
queries and bodies. By the time it reached the reports, its own writes had stored branch Z's texts
in the attacker's records, so the marker check (which forgives values the attacker stored) scrubbed
them. A branch-confidential report reached with valid, own parameters was never judged at all.

## Decision

`OwnScopeReads` (tests/Erp.Gates.Tests/G1) runs inside `CompanyAttack.RunAsync`, for both the
company and the branch layer, right after the victim snapshot and before any attack write, so no
value of the victim can yet be the attacker's own data. As each attacker (the scoped administrator
and the read-only user) it calls every GET of the running app:

- with no parameters;
- with its own company (and, in the branch layer, its own branch) in every documented parameter
  that names a company or branch (`company`, `companyId`, `branch`, `branchId`);
- with every record of the lookup list a report parameter draws from, as the attacker lists it;
- with `companyId eq '<own>'` (and the branch) in the filter of every list whose columns reference
  a company or branch (lists and their printed versions);
- in every export format the endpoint documents, in Arabic, and grouped by every groupable column;
- following list pages to the end (up to ten);
- then every route with one id parameter, with the ids the attacker's own answers showed (those of
  the route's own collection first, at most 60 per route): a user's access, a role, a record's own
  screen.

Every answer (decoded: CSV, XLSX and PDF as their text) must contain none of the victim's markers,
taken from the database before the attack. The gate tests require the sweep to have answered the
company profile and branch directory reports, the branch list print, the access screen by user id
and the company and user record screens, and the ratchet holds the request count
(`g1.branchOwnReadRequests`, `g1.companyOwnReadRequests`).

The leaky module carries three plants of the same kind (bugs 60 to 62: a branch directory report with
no parameters, a company's branches by its company parameter, a user's branch options by user id),
each reading with SQL of its own that row-level security limits to the company scope only. The gate
self-tests require the sweep to report each, in CSV and PDF as well.

## Why this shape

- Generic, not a list: a later module's "by company" report, export or per-record screen is swept
  without anyone naming it, exactly as the existing attack enumerates endpoints from the running app.
- Cheap: about 2,000 requests per layer (the branch attack is about 13,600), so the verify's
  processor-time maximum is not at risk. It shares the attack's environment; no new run.
- Run first, so the "values the attacker stored" exclusion cannot hide anything.

## Checked

With the critic's plants (plants-B1-B3-B3b.diff) applied, G1BranchScopeAttackTests failed with 114
leaks, among them `GET /api/reports/run/tenancy.branchDirectory`, `...companyProfile?company=<X>`
in every format and `GET /api/tenancy/access/{viewer}`; on the clean product it passes.
