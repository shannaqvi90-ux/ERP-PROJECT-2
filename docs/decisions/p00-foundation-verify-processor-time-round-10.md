# p00: `./erp verify` processor time, round 10 (two candidate cuts measured and rejected)

Date: 2026-10-11 (round 10). Piece: p00-foundation. Status: accepted.

## Context (routed by the lead, item 1)

Integration verifies used 10,356 to 11,005 processor seconds against the owner's maximum of
10,500 (`verify.cpuSeconds`, unchanged); the round-9 critic's run used 10,313 at a load of up to
95. The cut must come from the shared machinery, with nothing removed, narrowed or sampled away,
measured before and after.

## Measured: two candidates, side by side, same load

Method as in round 9: the same commit twice (or base and candidate), each in a toolbox container
of its own started together, the verify .NET stage's runtime settings, the same test filters in
the same order, processor seconds of the test runs only (the container's cgroup), the build
excluded (`ab.sh`, kept with the logs, not committed).

1. **Garbage collector settings** (round 9 measured these on the identity and tenancy tests only;
   the attack self-tests allocate far more): `DOTNET_GCgen0size` 256 MB instead of 64 MB, and
   `DOTNET_gcConcurrent=0`. 2026-10-11 07:22-07:43 UTC, load 38-52.

   | Tests | Verify settings | Candidate |
   |---|---|---|
   | company attack self-test (`Process=self-company`) | 773 | 779 |
   | identity module tests (100) | 42 | 41 |

   No difference beyond noise. Rejected; the verify settings stay as they are.

2. **The gate's PDF reader made each stream's text once** (`ResponseText`: the replacement texts
   and Latin-1 bytes of a stream kept by its SHA-256 in a bounded store, the same text returned
   for every answer). 2026-10-11 07:44-08:03 UTC, load 29-37.

   | Tests | Base | Candidate |
   |---|---|---|
   | company attack self-test | 750 | 755 |
   | G2 report data (prints every report and list in every format) | 67 | 68 |

   No difference: inflating and decoding the streams is not where the attack's time goes.
   Reverted (commit "Revert the gate PDF reader's per-stream store").

## What this round adds

The new checks (G2 matrices per session carrier, the takeover check's documented query values
and its plant self-test, the Tab-cycle end-to-end tests) add work. Their cost on this machine is
in the whole-verify figures below.

## Whole verify of this branch

`./erp verify` of this branch (`7b81fe2`), through the verify slots, slot taken 09:27:07 UTC,
passed in 4,032 s: every test passed (.NET 602, web unit 450, end to end 100, harness 373, verify
machinery 6) and the ratchet passed. **9,970 processor seconds** (.NET 7,917, web 1,768, end to
end 240, timing 46) against the maximum of 10,500. The machine was busy (load 31.97 at the start,
18.2 at the end, highest 77.61 over 271 readings), so wall time was not judged. An earlier run of
the same work an hour before (one gate test failing on a reviewed-list entry, fixed since) used
8,177 (.NET), 1,724 (web) and 264 (end to end) at loads up to 69.

No quiet hour came this round either; the next quiet verify (integrator or critic) gives one.
