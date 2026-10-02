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
