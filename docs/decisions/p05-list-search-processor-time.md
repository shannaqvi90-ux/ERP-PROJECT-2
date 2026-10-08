# p05 — taking processor time out of ./erp verify (round 7)

Date: 2026-10-09. Piece: p05-list-search (round 7). Status: accepted.

## Context

Round 6 of p05 passed every test at integration but `./erp verify` used 9,884 processor seconds
against the ratchet's maximum `verify.cpuSeconds` of 9,000 (dotnet 8,218, web 1,338, e2e 254,
timing 74; load 54 at the start). The integration base alone already measured 7,746 to 9,227 s
depending on load. The maximum may not be raised by a builder (CLAUDE.md rule 9; the owner later
raised it to 10,500 himself, needs-human #12), so the cost had to come out of how the same work
runs: no test, check, plant, request, value or ratchet minimum is removed or narrowed.

## Where the time went (measured on this machine)

The .NET stage of this branch run alone through the verify slot, each test process's processor
time read from `/proc/<pid>/stat` inside the stage container (load 30-70 from other agents):

| Process | Processor s |
|---|---|
| build | 175 |
| HTTP attack self-test (`self-http`) | 4,209 |
| suite gate process (G1 HTTP attack, non-interference, company attack, G2, rules) | 2,700 |
| company attack self-test | 767 |
| other suite assemblies and the non-interference self-test | about 700 |

The web stage, step by step in a container: vitest 75, the four plant self-tests 544 (client
isolation 51, identity 93, tenancy 179, forms 221), comparison harness 503, the rest about 30.

`perf` (49 Hz, call graphs, `DOTNET_PerfMapEnabled=1`) on the G1 HTTP attack and on the HTTP
self-test showed no single hot spot: request handling spread over EF Core, Npgsql, dependency
injection and the kernel. PBKDF2 for sign-ins was about 15% of the self-test (libcrypto) and is the
product's security setting (left as it is, as decision p03-identity-gate-processor-time already
argued). The avoidable costs found:

1. **Rows in memory queried through `AsQueryable()`** (the roles list, and the leaky module's
   lists): `EnumerableQuery` turns every query it runs into a new dynamic method that the runtime
   then compiles to machine code, three times per list request (count, groups, page). About 5% of
   the HTTP self-test (expression compilation, `DynamicMethod.CreateDelegate` and the JIT).
2. **Two EF queries on every authenticated request** (the workspace check in `TenantDirectory`
   and the user's company access in `CompanyScopeBinder`): EF's per-query work (cache key,
   funcletizer, shaper) and the first use of the tenancy context (resolving its scoped services)
   cost more than the statements themselves. About 13% of the self-test sat under
   `TenantDirectory.GetCurrentAsync` alone, 3.3% of it in starting the context.
3. **The differential check normalised each answer twice** (`Normalize(Normalize(text, value),
   control)` for both answers of every pair): a JSON parse, a tree with a copy of every array item
   and a serialisation per pass, and the scrubbed value's JSON and URI forms worked out again for
   every string in the body. About 3.4% of the self-test.
4. **Plant self-tests ran the whole gate after the plant was caught**: a planted copy only has to
   fail one assertion.

## Decision

1. `InMemoryQuery` (kernel lists): a list over rows in memory is run by a small query provider
   that rewrites the query's `Queryable` calls to the matching `Enumerable` calls (lambdas as
   delegates) and runs the result with the expression interpreter
   (`Compile(preferInterpretation: true)`). Same operators, same results; no code is emitted.
   The map from each `Queryable` method to its `Enumerable` counterpart is built once when a list
   registers as in memory and never changes afterwards (the first version filled it per request
   and the process-state gate refused it: correct, and fixed). Tests compare its answers with LINQ
   to objects over many queries, and list pages over rows in memory with the engine's own
   `Apply` over `AsQueryable()`.
2. `TenantDirectory.GetCurrentAsync` and the company-access read of `CompanyScopeBinder` are direct
   statements on the unit of work's own connection and transaction, as `SessionGrants` already
   is: same rows, same row-level security, and the bound tenant named in the statement as the
   second layer (the EF queries carried it through the context's tenant filter).
3. The HTTP attack's differential check normalises each answer in one streaming pass
   (`Utf8JsonReader` to `Utf8JsonWriter`): both values scrubbed from every string value (never
   from a property name), every `traceId` property dropped, the rest written as
   `JsonNode.ToJsonString` writes it. A body that is not JSON is scrubbed as text exactly as
   before. A test compares the one pass with the two passes over JSON and non-JSON bodies, both
   value orders.
4. Planted runs of the four web plant self-tests pass `--bail=1` to vitest: the run stops at the
   first failed test. The plant still counts as caught only when the gate fails on an assertion
   (a run that stops on any other error fails the self-test, as before), and the unplanted control
   runs the whole gate.

## Measurements

The G1 HTTP attack alone, the integration-merged branch before this round's changes and with
changes 1 and 2, side by side on the same machine at the same time (load 30-100): **2,439 → 1,914
processor seconds** (−21%), every count the attack reports still at or above its ratchet minimum
(the test asserts them). Identity plant self-test (user+sys on the host): 122 → 81 s, every plant
caught.

The full verify of this round is in the round's notes for the integrator.

## Rejected

- Raising `verify.cpuSeconds`: a builder may not (rule 9).
- Pacing tenant B's concurrent reader (44,295 requests in one attack, ratchet minimum 3,000):
  fewer interleavings would be a weaker check.
- Skipping the stack walk that names the code sending a settings statement (about 3%): the
  settings rule needs it for every such statement.
- Pooling EF contexts to save their start-up on every request: the options depend on the unit of
  work's connection, and a pooled context carried between requests is exactly the kind of state
  the isolation gates exist to refuse.
- Caching proofs in the leaky module's planted sign-in lookup: new process-wide state in the
  plant would change what the process-state self-tests see.
- A larger first GC generation for the test processes: not measured to a conclusion (the run was
  stopped to keep memory free for other agents).
