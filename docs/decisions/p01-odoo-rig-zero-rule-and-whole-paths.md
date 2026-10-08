# p01: both-at-zero counts left out; ours judged on whole paths; mutation controls shared

Date: 2026-10-08 (round 8)

## Context

- **Owner decision, needs-human #11** (gauntlet/goal.md, bar item 2, commit ac3f3e7): a count
  metric (steps, keystrokes, clicks, field entries and the like, never time) on which both
  products score exactly 0 is left out of that task's comparison, neither a tie nor a win. Every
  other metric must still be strictly better for ours; any other tie is a loss; if every metric
  ties, the task is a loss; the verdict names the metrics left out. Round 7 reported such a
  metric as "tie at zero" and counted it as a loss, pending the owner's answer.
- **p00 critic, round 7 (sign-in):** the comparison's headline for ours took steps from one of
  its expert paths (`new-device-whole-e-mail`) and keystrokes and human seconds from another
  (`new-device`). No single path achieves that headline.
- **p02 critic, round 6 (switch-company):** the signed-in person's name (Mariam Al Mansoori) and
  company codes in the top bar showed in ours' blind shots.
- **Integrator, round 7:** the verify used 9,227 processor seconds against the ratchet's maximum
  of 9,000. The new mutation step in the web stage ran one control and one mutated run per
  mutation, each starting a browser.

## Decisions

1. **The zero rule lives in `comparePath` (lib/runner.mjs).** `COUNT_METRICS` names the count
   metrics the harness measures: `steps` and `keystrokes`. A metric is left out only when it is a
   count metric and both values are exactly 0 (`=== 0`). Its outcome reads `left out (both exactly
   0)` and it is listed in `left_out`. The other metrics decide: the verdict is a win only when at
   least one metric is counted and every counted metric is strictly lower for ours. A tie on any
   counted metric, including a time metric at 0 on both sides, is a loss. The comparison output
   names the metrics left out (`left_out`, `left_out_note`), and `run.mjs` prints them. The round
   7 fields (`ties_at_zero`, `tie_at_zero_note`, `loss_only_from_ties_at_zero`) and the open
   question are removed. The round 7 test that kept a tie at zero as a loss is replaced by one
   test per limit of the decision. This follows the owner's change to the bar; it is not a test
   weakened to get a pass.

2. **Ours is judged on whole paths.** `compareRuns` judges each verified path of ours on its own
   counts, and ours wins when one path wins by itself. The comparison shows that path
   (`ours_path`), or when none wins, the path that wins the most metrics. It lists every path's
   verdict (`ours_paths`). Ours' result file counts one path too (`counts_path`, the path its
   shots come from). The reference keeps counting each metric from its best expert path. Beating
   that is the same as beating each of its paths whole, so the reference is never weaker than any
   path an expert could take. Over repeats, `medianOf` keeps each path's own median times
   (`median_counts`), so a path is never judged on another path's clock.

3. **Masks:** every product's shots also mask the demo people's names (English and Arabic), the
   reference's people (`Amal Approver`, `Bilal Buyer`, `Arabic Reporter`, and `Administrator` as
   a whole text only) and its second company (`Demo Manufacturing FZE`), and the task fixtures'
   company codes (`DEMO-TRD`, `DEMO-MFG`) and Arabic names. As in round 7, the lists are the union
   for both products. The existing browser test checks the new names on the page it already
   opens, with no extra browser.

4. **Mutation controls are shared per test file.** `scripts/mutations.mjs` runs the self-tests
   of each test file once on the unmutated copy, for the union of its mutations' name patterns,
   and reads each test's result by name from the TAP output. A mutation counts as caught only
   when a test that passed in that control fails mutated. When a test it runs did not pass in the
   control, the mutation is reported as not judged, which still fails. That is 6 controls instead
   of 16. Measured in the verify toolbox image (cgroup processor time, as `./erp verify` measures
   it), the mutation step went from about 190 s (16 mutations) to 104 s (20 mutations). M17-M20
   plant faults in the new comparison rules (a time metric left out, any count tie left out, a
   tie counted as a win, ours judged on the best of each metric across its paths). These cost
   well under a second each, because `test/compare.test.mjs` starts no browser.

## Alternatives not taken

- Holding the reference to a single path as well: its best path differs by metric (fewest keys
  versus fastest), and judging ours against one reference path picked by the harness would let
  ours beat a path no expert would choose for that metric.
- Using the main suite's run as the control: the mutations run in a scratch copy, and the control
  exists to catch a copy where tests fail anyway. A control in the same copy keeps that.
