# p00 round 13: the gate self-tests attack the planted surface; partial bodies; state outside the process

Date: 2026-10-18. Piece: p00-foundation. Status: accepted.

## 1. `./erp verify` processor time (critic p00 round 12, biggest gap; lead item 3)

The round-12 critic's clean-clone verify passed every test and was still failed by the ratchet on
processor time (10,726 s against the owner's maximum of 10,500); its second run passed with 10,433.
The .NET stage was 7,987 to 8,136 s of it.

### Where the .NET stage's time went (measured)

The stage runs four test processes side by side (`build/verify-inside.sh`): the suite and three
self-tests of the planted module, each alone so their planted static state never meets. From the
critic's failed run (`verify-failed-run1`, its test reports):

| Test | Wall time |
|---|---|
| self-http: the HTTP attack's self-test (one test) | 3,503 s |
| suite: G1 HTTP attack (the gate) | 2,238 s |
| suite: G1 non-interference (the gate) | 1,065 s |
| self-company: the company and branch attacks' self-tests | 1,034 s |
| self-noninterference: the non-interference self-test | 853 s |
| every other test of the suite together | about 2,400 s |

The self-test of the HTTP attack ran the whole attack over every endpoint the app exposes, the
product's (120) and the planted module's (88) alike: a second full attack of the product, in the
same verify that attacks the product in the gate itself. The company and non-interference
self-tests did the same with their checks. Round 9's process report had measured the HTTP self-test
alone at 4,777 processor seconds of the stage's 7,646.

### Decision: the self-tests attack the planted surface; the gates attack everything

`IsolationAttack.RunAsync`, `CompanyAttack.RunAsync` and `NonInterference.RunAsync` take an
endpoint scope (`only`, default: every endpoint). The gates (`G1HttpIsolationTests`,
`G1CompanyIsolationTests`, `G1BranchScopeAttackTests`, `G1NonInterferenceTests`) do not pass one and
run exactly as before, with every ratchet minimum. The three self-tests pass
`GateSelfTests.PlantedEndpoint`: the planted module's own routes and the kernel's routes for its
lists and reports (views, definitions, printing, running a report), which is the surface every plant
is on. Inside a scoped run every phase, judgement and blind-spot check is the gate's own; the lists
attacked through their query contract, the list answers and the isolation probes follow the
endpoints in scope. No plant, assertion or check was removed: every plant is still asserted caught,
and each scoped self-test now also asserts that it attacked every planted endpoint (the planted
surface counted from the running app), so a scope that lost an endpoint fails. The ratchet's
`rules.listsChecked` minimum is the gate's and is applied when the attack is unscoped. Precedent: the
self-tests already judged only the planted lists sorted (round 8), and the takeover, write-set,
subject and volume self-tests already ran on the planted endpoints only.

What the self-tests no longer do is attack the product's 120 endpoints a second time with the
planted module loaded. Their assertions that nothing outside the planted module leaks are still
there and still run; the product's own surface is judged by the gates themselves in the same verify.

One correction this needed in the gate machinery: the process-state fingerprint
(`ReachableState.Fingerprint`) read the private fields of expression trees, and a lambda keeps its
first parameter in a field until its parameter list is first read, then the list. In the scoped
self-test the product's list bindings were first used after the first fingerprint, and every list
selector "changed". A framework expression tree is now read by its text and by the objects its
constants (closures) hold, which are walked like any other object; extension nodes keep the field
walk. `ReachableStateFingerprintTests` proves a count kept in a selector's closure is still seen and
the framework's first-use caches are not.

### Measured before and after

Method as in rounds 9 and 10: the base commit (`3e1fd03b`, the integration branch this round
started from) and this branch, each in a toolbox container of its own started together, with the
verify .NET stage's runtime settings and the same test filters in the same order; processor seconds
of the test runs only (the container's cgroup), the build excluded. Every base test passed. On this
branch's first run the HTTP self-test failed on its last assertion only (the fingerprint correction
above), with every plant caught; the final code's runs (last column, 21:44-22:08 UTC, after the
base had finished, so not side by side) passed.

The self-tests, 2026-10-18 20:11-21:44 UTC, load 13-53:

| Test process | Base | This branch | Final code (22:08) |
|---|---|---|---|
| self-http (the HTTP attack's self-test) | 4,045 s (wall 3,752) | 1,241 s (wall 977) | 1,228 s (wall 870) |
| self-company (company and branch attacks' self-tests) | 708 s | 282 s | 271 s |
| self-noninterference | 390 s | 135 s | 171 s |
| **total** | **5,143 s** | **1,658 s** | **1,670 s** |

About 3,470 processor seconds less, two thirds of the three processes. The longest process of the
.NET stage is now the suite (the gates), not the HTTP self-test.

The gates this round made stricter (section 2), 2026-10-18 22:14-23:12 UTC, load 15-69, the three
gate classes in one test process:

| Tests | Base | This branch |
|---|---|---|
| G1 HTTP attack, G1 non-interference, G1 shell answers | 2,272 s (wall 2,817) | 2,521 s (wall 3,399) |

The partial bodies cost 249 processor seconds there (the HTTP attack's 617,174 requests became
639,231; the non-interference check's 442 write comparisons became 666). A first version kept every
record a partial create made as a route value of tenant B's, which the attack sends in every route:
744,600 requests; it no longer does (a partial create's record is one more record of a table whose
records the activity already uses in routes).

## 2. Partial bodies (lead item 1, from critic p04 round 11)

Every body the G1 machinery sent carried every field: `TenantActivity`'s variants fill every
enumerated field, and the shell answers check sent `{language, numerals}`. The product's own screens
send partial bodies (Alt+L sends `{language}` alone, the digits switch `{numerals}` alone), and the
critic's plant SL1 (the digits last chosen by anyone, kept in a file, answered a language-only
switch) passed every G1 check.

Decision:

- `OpenApiDocument.OmittableFields`: the top-level fields a client may leave out, the ones the
  document does not require and the ones whose schema allows null (the binder reads a missing field
  as null; the preferences body documents both fields as required and nullable, and the shell leaves
  either out).
- `TenantActivity.PartialVariantsOf`: for every write with such fields, the body without each one in
  turn; with several, the body without any of them; and when every field may be left out (a
  settings write such as the preferences), each documented value of each enumerated field alone, the
  first value again last (a write about the caller's own settings leaves the caller as it found it).
  A partial body the product refuses with a client error is a refusal, judged like any answer but not
  a failed own write; a server error is still a failure.
- Both tenants' own writes (before the HTTP attack and before the non-interference comparison) send
  every partial body. The write-after-write phase sends each partial body the product accepted from
  tenant B, right after the other tenant wrote the whole body (its own texts in every field), both
  ways, judged for the other tenant's markers. The non-interference check compares every accepted
  partial body like any write (shared process against a fresh one), and judges each one that leaves
  out an enumerated field against the other tenant's choice of it
  (`JudgePartialAfterOthersChoiceAsync`): both tenants write the first values, the judged tenant
  sends the partial body twice, the other tenant writes the left-out field at its last documented
  value, and the judged tenant's answer must not follow that choice back and forth. That comparison
  stays in the shared process because state kept outside the process reaches a fresh process too.
- The shell answers check runs the shell's own two switches, `{language: "ar"}` alone after the other
  tenant's `{numerals: "latn"}` alone and `{numerals: "arab"}` alone after the other's
  `{language: "en"}` alone, each judged tenant having first chosen Arabic with Arabic-Indic digits.
- Self-test plant bug 72 (SL1's shape, in the planted module): `PUT /api/leaky/me/display` keeps the
  digits last chosen by anyone in a temporary file and answers a body without digits with them. The
  non-interference self-test requires it caught both ways, on the bodies that leave the digits out
  only.
- Ratchet: `g1.partialWriteVariants`, `g1.partialWritePairs`, `g1.partialWriteEndpoints`,
  `g1.nonInterferencePartialWriteComparisons`, `g1.nonInterferencePartialChoiceChecks` (values from
  this round's passing run).

## 3. State outside the process (lead item 2, from critic p05 round 12)

The tenant-source scanner and the process-state inventory look inside the process: fields,
singletons, framework stores. The critic's plant L9 kept list totals in temporary files keyed
without the tenant; only the live checks caught it. A file, a memory-mapped file, a named mutex,
semaphore or event, a pipe, a socket, a child process, unmanaged memory or a native call holds state
neither static layer sees, and a second process (another server, the non-interference check's fresh
process) shares it.

Decision:

- Compiled-code gate `G1OutsideProcessStateTests`: every method of the product's compiled code
  (9,183 methods, 60,039 calls resolved) that calls such an API is reviewed one by one in
  `tests/Gates/outside-process-methods.txt`; any other fails, and so does a reviewed entry that no
  longer calls one. It reads call instructions, so an alias or a helper in another file does not hide
  a use; async state machines, lambdas and local functions count for the method they are written in.
  Two methods are reviewed: the operator's seeding from the shared dataset's CSV and the PDF font
  face that copies an embedded font into unmanaged memory once. Its self-test plants 20 shapes
  (temporary files, file streams and infos, directories, readers and writers on a path, a
  memory-mapped file, a named mutex, semaphore and event, a named pipe, a socket, a child process,
  unmanaged memory, a native call) and requires each found, and requires streams over memory or a
  response, unnamed synchronisation objects and path arithmetic not to be.
- Source rule `outside-process` in the tenant-source scanner (`tests/Gates/tenant-bypass-sources.txt`),
  with planted examples of each shape; three files reviewed by count (the seeding CSV reader, the two
  stream writers of the exports, which write to a stream the caller holds, and the font copy).
- Ratchet: `g1.outsideProcessMethodsScanned` 9,100, `g1.outsideProcessPlantsCaught` 20,
  `g1.tenantSourceRules` 14 (was 13).

## Processor time this round adds

Measured side by side (section 1): the partial bodies add 249 processor seconds to the three gate
classes they touch (2,272 to 2,521 s); the compiled-code scan, the source rule and the fingerprint
test add about 2 s. Against that, the scoped self-tests take about 3,470 s less.

The whole `./erp verify` of this branch (`7e9652dd` plus this record), through the verify slots,
slot taken 2026-10-19 00:31:06 UTC: **verify passed in 3,466 s**, every test passing (.NET 700, web
unit 522, end to end 111, comparison harness 415, verify machinery 6) and the ratchet passed.
**Processor time 7,171 s** (.NET 4,811, web 1,967, end to end 356, timing 37) against the owner's
maximum of 10,500 (unchanged), where the round-12 critic's clean-clone runs used 10,726 and 10,433
(.NET 8,136 and 7,987). The .NET stage took 2,795 s of wall time. The machine was busy (load up to
57.6 over 233 readings), so wall time was not judged. `suite.dotnetTests` is raised to 700.

Nothing was removed, skipped or narrowed in the gates: every gate test, plant, assertion and ratchet
minimum is as before or stricter, and the processor-time maximum is the owner's and untouched.
