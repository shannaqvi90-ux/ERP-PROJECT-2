import test from 'node:test';
import assert from 'node:assert/strict';
import { METRICS, compareRuns, medianOf } from '../lib/runner.mjs';

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

test('a tie at zero is still a tie and a loss, and is reported plainly (owner question pending)', () => {
  const ours = counts(1); ours.keystrokes = 0;
  const odoo = counts(2); odoo.keystrokes = 0;
  const r = compareRuns(run('ours', ours), run('odoo', odoo));
  // The owner's rule is applied unchanged.
  assert.equal(r.verdict, 'loss');
  assert.match(r.metrics.keystrokes.outcome, /tie at zero \(a tie is a loss\)/);
  assert.equal(r.metrics.keystrokes.tie_at_zero, true);
  assert.deepEqual(r.ties_at_zero, ['keystrokes']);
  assert.equal(r.loss_only_from_ties_at_zero, true);
  assert.match(r.tie_at_zero_note, /keystrokes: both products score 0/);
  assert.match(r.tie_at_zero_note, /Should a metric on which both products score 0 count toward the tie rule\?/);
  // A tie at zero beside a real loss is reported, but the loss does not come from it alone.
  ours.steps = 3;
  const r2 = compareRuns(run('ours', ours), run('odoo', odoo));
  assert.equal(r2.verdict, 'loss');
  assert.equal(r2.loss_only_from_ties_at_zero, false);
  // An ordinary tie is not a tie at zero; no note when there is none.
  const r3 = compareRuns(run('ours', counts(2)), run('odoo', counts(2)));
  assert.deepEqual(r3.ties_at_zero, []);
  assert.equal(r3.tie_at_zero_note, undefined);
  assert.equal(r3.metrics.steps.tie_at_zero, undefined);
  // An unusable run is not comparable, never a tie at zero.
  const r4 = compareRuns(run('ours', counts(0), 'failed'), run('odoo', counts(0)));
  assert.deepEqual(r4.ties_at_zero, []);
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
