# p00 round 21: deterministic test listing, XLSX cells read as shown, e-mail draw, per-group processor seconds

Date: 2026-11-18. Piece: p00-foundation. Status: accepted. Routed fix round after the round-20 critic (BLOCKED: the
partition check failed at random).

## 1. The partition check's listing (the critic's biggest gap)

Cause: `dotnet test Erp.slnx --list-tests` runs the test projects side by side and they write to one stream, so
two names landed on one line or a line was cut; the check then reported missing and extra tests.

Decision: `build/list-tests.sh` (sourced by `build/verify-inside.sh`) lists **one project at a time** (one test
process, one writer), the solution as its `tests/*.csproj` entries in order, and parses strictly
(`list_parse`): exactly one header, a test line only after it, every line one `Namespace.Class.Method` with
optional theory arguments; anything else exits 3 with `LISTING BROKEN (<project>): <why>` and the check
stops with "The test listing is broken ... that is not a missing or an extra test". A project without tests
prints nothing and a filter matching nothing prints the header only; both are an empty set, not an error.
Rejected: `-m:1` on the solution (it would serialise the listing but a cut line could still pass as a name),
and a retry (a retry would hide a broken listing).

Proof: `build/list-tests.test.mjs` (9 tests, run by the web stage): good listing read exactly, empty cases,
six broken listings (two names on a line, a theory after a name, a log line in a name, a test before the
header, no header, two headers) all reported broken with exit 3, and a stand-in `dotnet` showing that one
broken project fails the whole listing. 20 listings of the whole solution (`--filter "Load!=Timing"`, 1,129
tests) with 16 busy-loop processes beside them (load 16 to 19) gave 20 identical results (one md5).
Cost: about 16 s of wall time per solution listing and about 3 s per gate listing, 13 listings, so roughly
1 minute and 50 processor seconds more than the old check.

## 2. G2ReportDataTests flagged '8800' in an XLSX date-time serial

Cause: `Exports.cs` writes a moment as its serial number with up to 8 decimals (`46344.88001234`) and the gate
read every `<v>` as text, so a data value `8800` was "printed" inside the serial.
Decision: `ReportDataCheck.XlsxText` now reads the workbook's styles and gives every cell with a date or time
format the date (or date and minute) it displays; number cells stay numbers and text cells stay text (a text
cell with a date style is still text). Self-tests (`G2XlsxCellTests`, 6): the serial is not text, the shown
moment and date are; a leaked value in a text cell, a number cell (with and without a format, and a formula
result) and a date cell is still in the judged text; a self-closed cell does not swallow the next one. The
planted-fault self-tests of the print gate (`PrintPlantModule`, `GateSelfTests` leaky.taxNumbers) are in the
targeted run and still must fail the planted reports.

## 3. The planted /api/leaky/lookup?email= leak sometimes missed

Cause (reasoned, not reproduced: the self-test passed in this round's run, 21.6 min): the query-parameter
attack sent a string parameter only the class representatives of tenant B's text values (first, middle and
last of each column's eight). An e-mail address can be taken into another column's class (the first table that
holds it wins, `audit` sorts first), leaving the users' class with addresses that do not sign in, and the
planted lookup finds an account only for an address with the gate password. Decision: for a parameter whose
name contains "mail", the draw also sends every e-mail-shaped value of tenant B it read (at most 40), each with
its control as before (`G1HttpIsolationTests.MailAddressesFor`): stricter, never fewer requests. The self-test
now fails with the number of leaks on the lookup and the attack's request counts for it, so a miss that still
happens names itself. If it recurs the next suspect is the lookup's request itself (a timeout or a 500 under
load), which that message shows.

## 4. Processor time between alone runs

Seen: the same tree 23,650 to 27,940 s (dotnet 20,543 to 24,854 s). Not found in this round: a verify takes
about 2 h and the stage's processor seconds were recorded for the stage only, so no run could say which group
varied. Measured instead: GC concurrency off (`DOTNET_gcConcurrent=0`) on the kernel tests, two runs each, 23.8
and 23.9 s with it on, 23.7 and 20.8 s off (the project is too small and the spread too big to decide), so
nothing was changed. Added: every test group writes its own processor seconds (user + system of the processes
its `dotnet test` waited for) to `$out/cpu-group-<name>`, a report next to `cpu-dotnet`, judged by nothing and
kept with the other `cpu-*` files. The next alone run therefore shows which group moves. The suspected source
is the three groups' oversubscription (spinning in the thread pool and `SemaphoreSlim` when more threads are
runnable than cores), which owner decision #51's cap of three groups already limits.
Processor time this round adds to a verify: about 50 s (listings) and nothing from the rest.

## Ratchet

`suite.dotnetTests` 1078 to 1135 (6 new tests, the listing counted 1,129 without Timing), `suite.buildTests`
13 to 22 (9 new). No maximum changed.
