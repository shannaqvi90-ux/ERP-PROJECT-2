import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { METRICS, bestPerMetric, comparePath, compareRuns, medianOf } from '../lib/runner.mjs';

const run = (product, counts, status = 'verified') => ({ product, task: 't', status, counts, result_file: `${product}.json` });
const counts = (n) => Object.fromEntries(METRICS.map(m => [m, n]));

test('ours wins only when strictly lower on every metric', () => {
  assert.equal(compareRuns(run('ours', counts(1)), run('odoo', counts(2))).verdict, 'win');
  const c = counts(1); c.keystrokes = 5;
  const r = compareRuns(run('ours', c), run('odoo', counts(2)));
  assert.equal(r.verdict, 'loss');
  assert.equal(r.metrics.keystrokes.outcome, 'loss');
});

test('a tie is a loss', () => {
  const c = counts(1); c.steps = 2;
  const r = compareRuns(run('ours', c), run('odoo', counts(2)));
  assert.equal(r.verdict, 'loss');
  assert.match(r.metrics.steps.outcome, /tie/);
});

// Owner decision 2026-10-08 (needs-human #11, gauntlet/goal.md bar item 2): a count metric on
// which both products score exactly 0 is left out of the comparison, neither a tie nor a win.
// One test per limit of that decision.
test('zero rule: a count metric on which both score exactly 0 is left out, neither a tie nor a win; the rest decides', () => {
  const ours = counts(1); ours.keystrokes = 0;
  const odoo = counts(2); odoo.keystrokes = 0;
  const r = compareRuns(run('ours', ours), run('odoo', odoo));
  assert.equal(r.verdict, 'win');
  assert.equal(r.metrics.keystrokes.left_out, true);
  assert.match(r.metrics.keystrokes.outcome, /left out \(both exactly 0\)/);
  assert.doesNotMatch(r.metrics.keystrokes.outcome, /win|tie/);
  assert.deepEqual(r.left_out, ['keystrokes']);
  // Both at 0 on both count metrics: each is left out, the times decide.
  ours.steps = 0; odoo.steps = 0;
  const both = compareRuns(run('ours', ours), run('odoo', odoo));
  assert.equal(both.verdict, 'win');
  assert.deepEqual(both.left_out, ['steps', 'keystrokes']);
  // Left out does not mean won: a loss elsewhere is still a loss.
  ours.machine_seconds = 3;
  assert.equal(compareRuns(run('ours', ours), run('odoo', odoo)).verdict, 'loss');
  // An unusable run is not comparable, so nothing is left out.
  const r4 = compareRuns(run('ours', counts(0), 'failed'), run('odoo', counts(0)));
  assert.deepEqual(r4.left_out, []);
  assert.match(r4.verdict, /ours failed/);
});

test('zero rule: only both exactly 0; 0 against anything else is compared as usual', () => {
  const zeroVsOne = counts(1); zeroVsOne.keystrokes = 0;
  const odoo = counts(2); odoo.keystrokes = 1;
  const r = compareRuns(run('ours', zeroVsOne), run('odoo', odoo));
  assert.equal(r.metrics.keystrokes.outcome, 'win');
  assert.equal(r.metrics.keystrokes.left_out, undefined);
  assert.deepEqual(r.left_out, []);
  assert.equal(r.verdict, 'win');
  // Ours above a reference at 0 loses that metric, and so the task.
  const ours = counts(1); ours.keystrokes = 1;
  const odooZero = counts(2); odooZero.keystrokes = 0;
  const r2 = compareRuns(run('ours', ours), run('odoo', odooZero));
  assert.equal(r2.metrics.keystrokes.outcome, 'loss');
  assert.equal(r2.verdict, 'loss');
  assert.deepEqual(r2.left_out, []);
  // Near 0 is not 0.
  const near = counts(1); near.steps = 0;
  const odooNear = counts(2); odooNear.steps = 0.001;
  assert.equal(compareRuns(run('ours', near), run('odoo', odooNear)).metrics.steps.outcome, 'win');
});

test('zero rule: count metrics only; a time metric equal on both sides still ties, even at 0, and the tie is a loss', () => {
  for (const m of ['machine_seconds', 'human_seconds', 'human_plus_wait_seconds']) {
    const ours = counts(1); ours[m] = 0;
    const odoo = counts(2); odoo[m] = 0;
    const r = compareRuns(run('ours', ours), run('odoo', odoo));
    assert.equal(r.verdict, 'loss', m);
    assert.match(r.metrics[m].outcome, /^tie/, m);
    assert.deepEqual(r.left_out, [], m);
  }
});

test('zero rule: any other tie is still a loss, beside a metric left out', () => {
  const ours = counts(1); ours.keystrokes = 0; ours.steps = 2;
  const odoo = counts(2); odoo.keystrokes = 0;
  const r = compareRuns(run('ours', ours), run('odoo', odoo));
  assert.equal(r.verdict, 'loss');
  assert.match(r.metrics.steps.outcome, /tie \(a tie is a loss\)/);
  assert.deepEqual(r.left_out, ['keystrokes']);
});

test('zero rule: when every metric ties the task is a loss, with or without metrics left out', () => {
  const r = compareRuns(run('ours', counts(2)), run('odoo', counts(2)));
  assert.equal(r.verdict, 'loss');
  for (const m of METRICS) assert.match(r.metrics[m].outcome, /^tie/, m);
  assert.deepEqual(r.left_out, []);
  // Count metrics both at 0 and every time tied: nothing is won, so a loss.
  const ours = counts(2); ours.steps = 0; ours.keystrokes = 0;
  const r2 = compareRuns(run('ours', ours), run('odoo', { ...ours }));
  assert.equal(r2.verdict, 'loss');
  assert.deepEqual(r2.left_out, ['steps', 'keystrokes']);
  // Everything at 0 (no metric left in a win): a loss.
  assert.equal(compareRuns(run('ours', counts(0)), run('odoo', counts(0))).verdict, 'loss');
  // Directly: counts left out and no time measured on either side is not a win.
  const direct = comparePath({ steps: 0, keystrokes: 0 }, { steps: 0, keystrokes: 0 });
  assert.equal(direct.verdict, 'loss');
  assert.deepEqual(direct.left_out, ['steps', 'keystrokes']);
  assert.equal(direct.metrics.machine_seconds.outcome, 'not_comparable');
});

test('zero rule: the comparison output names the metrics left out, in its fields and in words', () => {
  const ours = counts(1); ours.keystrokes = 0; ours.steps = 0;
  const odoo = counts(2); odoo.keystrokes = 0; odoo.steps = 0;
  const r = compareRuns(run('ours', ours), run('odoo', odoo));
  assert.deepEqual(r.left_out, ['steps', 'keystrokes']);
  assert.match(r.left_out_note, /Left out of this comparison: steps, keystrokes/);
  assert.match(r.left_out_note, /needs-human #11/);
  assert.match(r.rule, /exactly 0 is left out/);
  // No note and nothing named when nothing is left out; the old tie-at-zero reporting is gone.
  const r3 = compareRuns(run('ours', counts(1)), run('odoo', counts(2)));
  assert.deepEqual(r3.left_out, []);
  assert.equal(r3.left_out_note, undefined);
  for (const k of ['ties_at_zero', 'tie_at_zero_note', 'loss_only_from_ties_at_zero']) assert.equal(r3[k], undefined, k);
  // The command line names them too.
  const src = fs.readFileSync(new URL('../run.mjs', import.meta.url), 'utf8');
  assert.match(src, /left out: \$\{cmp\.left_out\.join\(', '\)\}/);
  assert.doesNotMatch(src, /TIE AT ZERO/);
});

// Round 8 (p00 critic, sign-in): ours' headline took steps from one path and seconds from another.
const pathRun = (product, variants) => ({ product, task: 't', status: 'verified', result_file: `${product}.json`,
  counts: Object.fromEntries(METRICS.map(m => [m, Math.min(...variants.map(v => v.counts[m]))])),
  variants: variants.map(v => ({ status: 'verified', ...v })) });

test('whole paths: ours wins only when one of its paths beats the reference on every metric by itself', () => {
  const fewKeys = { ...counts(1), human_seconds: 9, human_plus_wait_seconds: 9 };
  const fast = { ...counts(1), steps: 9, keystrokes: 9 };
  const odoo = run('odoo', counts(5));
  // Each path loses on something; the best of each metric across them would have won.
  const r = compareRuns(pathRun('ours', [{ id: 'few-keys', counts: fewKeys }, { id: 'fast', counts: fast }]), odoo);
  assert.equal(r.verdict, 'loss');
  assert.ok(['few-keys', 'fast'].includes(r.ours_path));
  for (const m of METRICS) assert.equal(r.metrics[m].ours, (r.ours_path === 'fast' ? fast : fewKeys)[m], `${m} is the shown path's own`);
  assert.deepEqual(r.ours_paths.map(p => [p.id, p.verdict]), [['few-keys', 'loss'], ['fast', 'loss']]);
  // A path that wins by itself wins the task and is the one shown.
  const r2 = compareRuns(pathRun('ours', [{ id: 'few-keys', counts: fewKeys }, { id: 'all', counts: counts(2) }]), odoo);
  assert.equal(r2.verdict, 'win');
  assert.equal(r2.ours_path, 'all');
  assert.deepEqual(Object.fromEntries(METRICS.map(m => [m, r2.metrics[m].ours])), counts(2));
  // A failed path is not judged; a run without paths is judged on its counts.
  const failed = pathRun('ours', [{ id: 'all', counts: counts(2) }, { id: 'broken', counts: counts(0) }]);
  failed.variants[1].status = 'failed';
  assert.equal(compareRuns(failed, odoo).ours_path, 'all');
  assert.equal(compareRuns(run('ours', counts(2)), odoo).ours_path, null);
});

test('whole paths: over repeats each path is judged on the medians of its own times', () => {
  const rep = s => pathRun('ours', [{ id: 'p', counts: { ...counts(1), machine_seconds: s, system_wait_seconds: 0, human_plus_wait_seconds: s } }]);
  const m = medianOf([rep(3), rep(1), rep(2)]);
  assert.equal(m.variants[0].median_counts.machine_seconds, 2);
  assert.equal(m.variants[0].median_counts.human_plus_wait_seconds, 2);
  const r = compareRuns(m, run('odoo', { ...counts(5), machine_seconds: 2.5 }));
  assert.equal(r.metrics.machine_seconds.ours, 2);
});

test('an unbuilt or failed product is never a win', () => {
  assert.equal(compareRuns(run('ours', null, 'not_built'), run('odoo', counts(2))).verdict, 'not_built');
  assert.match(compareRuns(run('ours', counts(1), 'failed'), run('odoo', counts(2))).verdict, /ours failed/);
  assert.match(compareRuns(run('ours', counts(1)), run('odoo', counts(2), 'error')).verdict, /odoo error/);
});

test('median of repeated runs uses the median machine seconds', () => {
  const rs = [3, 1, 2].map(s => ({ ...run('odoo', { ...counts(5), machine_seconds: s, system_wait_seconds: s, human_plus_wait_seconds: s }) }));
  const m = medianOf(rs);
  assert.equal(m.counts.machine_seconds, 2);
  assert.equal(m.repeats, 3);
});

// Round 9 (critic p01 r8, mutation A16): the reference is measured on its best path for each
// metric, never on a worse one; ours keeps the counts of its shown path.
test('the reference is held at its best path on every metric; ours keeps its shown path whole', () => {
  const v = (id, steps, keystrokes, machine, human) => ({ id, counts: { steps, keystrokes, machine_seconds: machine, system_wait_seconds: machine / 2, human_seconds: human, human_plus_wait_seconds: human + machine / 2 } });
  const paths = [v('a', 5, 10, 3, 9), v('b', 6, 4, 2, 12), v('c', 7, 20, 1, 8)];
  const odoo = bestPerMetric(paths, 'odoo', paths[0].counts);
  assert.deepEqual([odoo.counts.steps, odoo.counts.keystrokes, odoo.counts.machine_seconds, odoo.counts.human_seconds], [5, 4, 1, 8]);
  assert.equal(odoo.counts.system_wait_seconds, 0.5, 'the system wait of the path whose machine seconds count');
  assert.deepEqual(odoo.best_path_per_metric, { steps: 'a', keystrokes: 'b', machine_seconds: 'c', human_seconds: 'c', human_plus_wait_seconds: 'c' });
  const ours = bestPerMetric(paths, 'ours', paths[1].counts);
  assert.deepEqual(ours.counts, paths[1].counts, 'ours: the shown path, whole');
  assert.equal(ours.best_path_per_metric.keystrokes, 'b');
});
