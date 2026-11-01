# p00: the cost of the shared gates, round 15

Date: 2026-11-01 (round 15, routed by the lead). Piece: p00-foundation. Status: accepted, with a
shortfall against the 40 % aim that is stated below and not hidden.

## Why

In serial-12d four pieces (p07, p10, p12, p16) each passed every test in an integrator's alone run
and each was refused only by `verify.cpuSeconds` (10,600 s). The dotnet stage grows with every
endpoint, parameter and list column because the shared gates attack each of them. The round was
to cut that cost without weakening a gate (CLAUDE.md rule 9): no ratchet minimum lowered, no
endpoint, parameter, attacker, credential carrier, value kind or oracle dropped, no random
sampling, no time-out, re-run or skip. It also had to fix two false-positive sources of the
gates (the random control of the differential oracle, a flaky self-test).

## Measured before (integration branch 6b36e2d4, no p07, p12 or p16)

Method: the built `Erp.Gates.Tests` (Release) run on the host in the four processes
`./erp verify` uses (the suite without the three long self-tests, and `Process=self-http`,
`self-company`, `self-noninterference`), side by side, with the verify stage's runtime settings
(`DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0`, `DOTNET_GCgen0size=0x4000000`,
`DOTNET_TieredPGO=0`, `ERP_TEST_PASSWORD_ITERATIONS=10000`), `/usr/bin/time` for processor
seconds (user + system) and peak resident memory. The other test projects of the stage are not in
these numbers (they are small and unchanged).

| Process | CPU s before | wall s | peak RSS |
|---|---|---|---|
| suite (every gate test except the three) | 6,399 | 5,373 | 15.9 GB |
| `self-http` (The_HTTP_attack_catches_planted_header_route_and_body_leaks) | 1,339 | 1,183 | 3.1 GB |
| `self-company` | 415 | 680 | 1.6 GB |
| `self-noninterference` | 236 | 600 | 0.6 GB |
| **Gate assembly** | **8,389** | | |

Per test (wall seconds from the TRX files of the same run): G1HttpIsolationTests 4,679;
G1NonInterferenceTests 1,834; self-http 1,166; self-company 631; NonInterferenceSelfTests 583;
G1ShellAnswersTests 513 (paced on purpose, little processor); G1ListVolumeTests 295 + 173;
G1CompanyIsolationTests 222.

G1HttpIsolationTests alone (same settings, nothing else running): 4,369 CPU s (user 3,389,
system 980), wall 3,954 s, peak RSS 10.1 GB, 1,269,449 requests by tenant A and 316,722 by tenant B
(about 2.75 ms of processor time per request, nine SQL statements each).
G1NonInterferenceTests alone: 456 CPU s (wall 947 s).

## Where the requests of the HTTP attack went (tenant A, before)

Counted by phase, endpoint, kind and language (new: `AttackState.CountKind`, written to
`ERP_GATE_REPORT_FILE` when set; the numbers below are from that report).

- Phase 1 (route values, tenant-switch headers and query): 533,000 requests (42 %). Every route
  value of tenant B (the ids of every table, the ids tenant B created, its code: about 1,100 per
  endpoint with a route) was sent through every one of six sessions in three variants. A GET
  endpoint with a route cost about 12,400 requests.
- Phase 2 (query and route parameters, each with a control value for a GET): 575,000 requests
  (45 %): the 13 undocumented query names times 76 probe values (278,000 with controls and the
  Arabic copies), documented string parameters with 416 values each (about 160,000), route ids.
- The rest: write pairs (75,000), registered lists (68,000), request bodies (55,000).

## What changed (each with the reason it loses nothing)

1. **Route values stand for their classes** (`RouteValueClasses`). A route value of tenant B is
   the id of a row of one of its tables (class: the table), its own id, its code, an id created
   after the snapshot, or other text. The administrator's cookie session still sends **every**
   value plainly (the request that reaches every handler), tenant B still opens every value first,
   and the replayed values keep their order. The requests that only ask where the tenant comes
   from or who is refused (the tenant-switch headers and query, the bearer token, the callers
   without a role, anonymous, the Arabic sessions) send the first, middle and last value of each
   class (all of a class of three or fewer). Tenant B's Arabic session opens only those. Reason:
   the session, the carrier and the refusal are settled before the route is handled, and the
   handler's lookup is the same code for every id of a table; the first, middle and last id of a
   table cover the companies and branches a seed spreads over it. Phase 2 does the same for the ids
   tenant B created (three per class beyond the ten of each table it already sends, themselves the
   first, middle and last of those ten).
2. **Text values stand for their column**: the eight values read from each table column are one
   class; documented string parameters and the list filter attacks send the first, middle and last
   of them (`VictimValues.StringClasses`). Every sampled id, every enumeration value and the
   undocumented names (which already send one value per column) are unchanged.
3. **Tenant B's ids as text are made once** (`BuildRequest` rebuilt a list of 1,000 strings for
   every request it built).
4. **Cheaper statement watching** (`StatementCapture`, `SqlTrace`): the answer to "does this
   statement change a setting" is kept per statement text (the pattern took 5 microseconds on a
   long query and was asked five times for each statement), one pass finds out whether a statement
   names a reviewed function, the data-source user is parsed once, and the trace keeps one copy of
   its repeated texts. Measured with the trace switched off (A/B on the self-http process, alone):
   the trace is 9 % of the processor time but 2 GB of the 2.8 GB the process holds.

Tried and removed, no gain: comparing the non-interference check's configuration in place instead
of reading it whole before and after every judged request (456 s before, 452 s after).

## Self-tests

The gate's own self-tests catch every planted fault with the thinner requests: the full
`GateSelfTests` set passes (the planted module's routes, headers, query names, bodies and lists
are attacked by the same code, with the same classes). New tests:

- `ControlCorpusTests` (4): a control that occurs in tenant A's data in any letter case, or in its
  own answer, is never used, even when every text of the value's shape is in the data (the control
  then grows).
- `RouteValueClassesTests` (3): each table keeps its first, middle and last id, classes of three or
  fewer are kept whole, created ids and text are classes of their own.

## The two false-positive fixes

- **Random control** (`G1HttpIsolationTests.ControlFor`): the control of the differential oracle
  is now a value of the attack value's shape that occurs, in any letter case, nowhere in the text
  of tenant A's rows and of the tables no tenant owns (read with the superuser,
  `ControlCorpus.ReadAsync`) and nowhere in tenant A's own unfiltered answer from the same
  endpoint, and is not the attack value. It is drawn again until it is so; when every text of the
  shape occurs (a one or two character value) it grows by characters of the same kind. It is drawn
  once per value (and answer), so the same control is used for the same value on every endpoint.
  The oracle is as strict as before (a control still exists nowhere) and cannot fire by chance:
  the four cases seen (a two-letter control inside the numbering prefixes SO-, QT-, ..., a
  six-digit control inside a contact number, the roles filter) are exactly what the corpus holds.
  The enumeration branch (another published value no tenant holds) is unchanged.
- **Flaky self-test** (`The_HTTP_attack_catches_planted_header_route_and_body_leaks`, line 336):
  the planted sorted-search total memory of `/api/leaky/scroll` was filled by the first run of the
  list answers, so a later run that sent the same search and sort found it already filled by the
  other tenant and could not show it, depending on which of tenant A's first words were new.
  `IsolationAttack.RunAsync` takes an optional `beforeEachListRun`; the self-test clears the
  planted memory there, so each of the four runs starts from an empty memory and every run that
  sends a search with different true totals shows it, in both directions. The gate itself passes
  nothing. The assertion is unchanged.

## Measured after

Two kinds of measurement, because the host-run numbers above were taken while other runs of
mine shared the machine (the baseline gate process ran beside a second attack process), which
inflates processor time (hyper-threads, spinning): they overstate the gain and are kept only for
the shape of it. The comparable pair is the two full `./erp verify` runs below, same machine, same
slot, each started on a quiet machine (load 1 to 3.6 at the start; the stages' own load 27 to 35):

| `./erp verify` | dotnet stage CPU s | whole verify CPU s | wall s |
|---|---|---|---|
| integration branch 6b36e2d4 (before) | **7,384** | 9,822 (web 1,981, e2e 419, timing 38) | 4,725 |
| this branch, an earlier commit of the round | 6,662 | 9,148 | 4,490 |
| this branch, final commit 771a09fd | **6,274** | **8,660** (web 1,930, e2e 418, timing 38) | 4,408 |

The dotnet stage fell by **1,110 s, 15 %** (the whole verify by 12 %). Run-to-run spread of the
same tree is about 1,000 s, so the two runs of this branch (6,662 and 6,274) bracket the real
figure; the 40 % aim of the round was **not reached**.

Per test, wall minutes inside the stage (before / after): G1HttpIsolationTests 53.3 / 45.7;
G1NonInterferenceTests 24.3 / 22.3; self-http 16.3 / 14.5; self-company 9.9 / 9.4;
NonInterferenceSelfTests 7.0 / 6.8; G1ShellAnswersTests 8.5 / 8.5 (paced); G1ListVolumeTests
4.7 / 3.8; G1CompanyIsolationTests 4.9 / 4.6. All passed in both runs.

The same attack alone on the host, nothing else running (the clean pair for the largest test):
G1HttpIsolationTests 4,369 CPU s, 1,269,449 requests by tenant A, 10.1 GB peak before (measured
beside another process); 2,802 CPU s, 708,739 requests, 9.6 GB peak with the first four cuts. The
final commit adds three more cuts (the bearer session, the filter values, the replayed ids) that
were measured only inside the verify above. The planted-module attack (self-http), alone: 971 CPU
s and 2.8 GB with the trace, 884 CPU s and 0.87 GB with the trace switched off (A/B); 329,527
requests before the last cut, 316,839 after.

Memory: an `Erp.Gates.Tests` process holds 15.9 GB at its peak in the suite process before and
15.6 GB after (it runs several attack classes at once; the trace queues are most of it), 10.1 GB
before and 9.6 GB after for the HTTP attack alone. The trace keeps one copy of its repeated texts
now; the gain in this process was not measured separately.

### Cost each new endpoint or list column adds (tenant A requests, 2.75 ms of processor time each
with tenant B's share; read from the cost report of the HTTP attack)

| Unit | Before | After |
|---|---|---|
| GET endpoint with a route (`{id}`), phase 1 | 12,400 requests | 4,500 (and the final cut: about 1,600) |
| GET endpoint without a route (13 undocumented names x 76 values x attack/control x 2 languages, plus its documented parameters) | about 14,000 | about 13,300 |
| list text column that can be filtered (330 values x 6 operators x attack/control) | 3,960 | 1,490 |
| list reference column that can be filtered | 344 | 344 |
| list column that can be sorted (forged cursors) | 146 | 146 |

A new route endpoint therefore costs about two thirds less; a new endpoint without a route costs
the same as before, and that is where a piece's cost now sits.

## What was not cut, and why (for the lead and the owner)

The cost of a new GET endpoint without a route is still about 14,000 requests, 40 seconds of
processor time, almost all of it the undocumented-name matrix of phase 2: 13 names times 76 values
(one per table column) times an attack and a control, in English and in Arabic. Every cut of it
that was found drops a value kind, an attacker or the control, which the round forbids:

- sending only identifiers to the names that select a tenant (tenantId, tenant, tenant_id,
  companyId, workspace) and only identifiers to id and userId would cut it by about a third, but a
  handler may accept a name or an e-mail there;
- Arabic copies for one name of each class (3 of 13) would cut another 12 % of tenant A's requests
  but lose a handler that reads a name only in its Arabic branch;
- one control answer per value shape instead of per value would cut about 10 %, but compares an
  attack with an answer taken minutes earlier;
- tenant B's background reader (110,000 requests, a tenth of the total) runs flat out for as long
  as the attack runs; a lower rate changes the interleaving the gate is meant to produce.

These are for the owner to decide (each weakens the gate in a way that can be named).

Not done, and why: gate-backlog row 3 (G2 and the Host header) is not cheap and is not part of the
cost cut. Sharing the thirty-odd application starts between test classes would save about 500 CPU
seconds but every class mutates its environment.
