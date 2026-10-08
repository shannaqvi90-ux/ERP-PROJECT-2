# p06 — Isolation and permission gates that see printing and exporting

Date: 2026-10-06. Piece: p06-form-report, round 2. Status: accepted.

## The gap (critic round 1)

A print store that keeps printed files by their download name ("Users 2026-10-05.pdf", the same
for every tenant) leaked tenant B's users list and roles PDF to tenant A, yet every gate passed:
tenant B never printed or exported anything, so nothing of B's ever sat in such a store when A
asked. And G2 accepted any permission a report definition named, so a company profile (licence,
tax number, address, branches) registered under the workplace switcher's permission passed.

## Decision

**G1, every shape of every answer.** A new phase of the HTTP attack takes every GET whose API
document enumerates how it answers (any query parameter with an `enum`: format, language,
numerals, disposition, groupBy, a report's choice parameters …) and builds its shapes: every
combination when there are at most 64, otherwise a greedy covering set in which every combination
of the values of any three parameters occurs (every format with every language with every digit
system). Tenant B asks for every shape on its own records first (administrator, bearer client and
read-only user), with no query and with a query of its own where the route takes free text; every
one of B's requests must succeed or the run records a blind spot. A route that needs a record
(a company profile) is given one of the asking tenant's own records. Tenant A then asks for the
same shapes on its own records — with no query, with B's very query, and with a query of its own —
while B keeps asking in the background; then B asks once more. A's answers are judged for B's
markers (PDF text, workbook cells, CSV, JSON, every header) and B's for A's. The phase is generic:
any later export (p14) with an enumerated format joins it with no new code.

**G1, the reports probe brings the victim.** `IsolationProbeContext` gains an optional `Victim`
client (additive). The reports probe now prints every report and list path in all four formats
and both languages, B first on the very same path, then A with and without tenant-switch headers.

**Self-test.** The leaky module's bug 50 keeps printed files on disk by download name (per test
process) and serves them again; the HTTP self-test requires the shape phase to report it.

**G2, reports judged by what they print.** For every report, users holding exactly its permission
(and again with each other read permission added) run it in English and Arabic for real records
(they work in every company, so only permissions decide). Every text value of the workspace's own
rows (four characters or more, not a product label) that the document prints — subject,
parameters, facts, group labels, cells, totals — must also be shown by some endpoint those
permissions open (lists paged to the end, every record's own GET for the ids the lists gave). A
value no other endpoint shows is data the permission does not grant. A parameter needing another
permission must be refused (400) to a caller without it. A report that printed nothing judged
fails as blind. The catalogue must list exactly the reports and printable lists the caller may
run, without columns and parameters that need a permission it lacks. Bug 51 (a report printing tax
numbers under the leaky module's permission) is the self-test. Run against the critic's plant P1
(company profile under `tenancy.workplace.read`), the check reports licence, tax number, address
and e-mail as printed without permission.

## What the check found in the product, and the fix

The company profile printed branches (code, name, city, phone) to readers of companies who may not
read branches; users by role printed role names to readers of users who may not read roles; the
branch directory printed companies' legal names to readers of branches. The report framework now
lets a column, fact or parameter declare a permission beyond the report's own
(`ReportColumn.Permission`, `ReportParameter.Permission`; the host refuses to start when it is not
in the catalogue). For a caller without it the column is left out of the document (and its
source is told not to read it, `ReportRun.Prints`), grouping by it falls back to none, the
document says what was left out (in its language), the catalogue does not show it, and the
parameter is refused. A source may also shape a value by the caller's permissions
(`ReportRun.Holds`): the branch directory prints a branch's company by code to every reader of
branches and with its legal name only to readers of companies.

## Ratchet

New minimums: `g1.shapeEndpoints`, `g1.shapeAttacks`, `g1.shapeVictimRequests`,
`g2.reportPermissionSets`, `g2.reportValuesJudged`.
