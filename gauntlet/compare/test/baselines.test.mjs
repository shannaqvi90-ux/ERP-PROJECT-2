// The committed Odoo baselines are what every later piece is judged against, so `./erp verify`
// re-checks them without the rig: each one was produced by the driver as it stands now (a driver
// changed without a fresh baseline fails), and its counts follow from its recorded steps under the
// keystroke-level model (a hand-edited or stale count fails). The rig itself is re-driven by
// `npm run test:live` (test/live-odoo.test.mjs).
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { BASELINE_DIR } from '../lib/config.mjs';
import { modelSteps, round } from '../lib/klm.mjs';
import { loadDriver, loadTasks } from '../lib/registry.mjs';
import { INSTRUMENT_VERSION, METRICS, driverFingerprint, endStateOf, matchesRequest } from '../lib/runner.mjs';

const read = id => {
  const f = path.join(BASELINE_DIR, 'tasks', `${id}.json`);
  return fs.existsSync(f) ? JSON.parse(fs.readFileSync(f, 'utf8')) : null;
};

/** Counts that follow from a list of recorded steps alone. */
function fromSteps(steps) {
  return {
    steps: steps.length,
    keystrokes: steps.reduce((s, x) => s + x.keystrokes, 0),
    human_seconds: modelSteps(steps).human_seconds,
  };
}

for (const task of await loadTasks()) {
  test(`${task.id}: the Odoo baseline was produced by the current driver and its counts follow from its steps`, async () => {
    const driver = await loadDriver('odoo', task.id);
    if (driver.built === false) return;
    const b = read(task.id);
    assert.ok(b, `${task.id}: no Odoo baseline; run node run.mjs --task ${task.id} --product odoo --repeat 3`);
    assert.equal(b.status, 'verified', `${task.id}: baseline ${b.status}`);
    assert.equal(b.product, 'odoo');
    assert.equal(b.instrument, INSTRUMENT_VERSION, `${task.id}: baseline taken with instrument ${b.instrument ?? 'before 3'}, current is ${INSTRUMENT_VERSION}; re-capture it`);
    assert.deepEqual(b.driver, driverFingerprint('odoo', task.id),
      `${task.id}: the driver changed after its baseline was taken; run node run.mjs --task ${task.id} --product odoo --repeat 3`);

    const runs = b.variants ? b.variants : [{ id: null, steps: b.steps, waits: b.waits, counts: b.counts, status: b.status }];
    if (driver.variants) {
      assert.deepEqual(runs.map(r => r.id), Object.keys(driver.variants), `${task.id}: baseline does not cover every expert path of the driver`);
    }
    for (const r of runs) {
      assert.equal(r.status, 'verified', `${task.id} ${r.id ?? ''}: ${r.status}`);
      const derived = fromSteps(r.steps);
      for (const [k, v] of Object.entries(derived)) assert.equal(r.counts[k], v, `${task.id} ${r.id ?? ''}: ${k} ${r.counts[k]} but its steps give ${v}`);
      assert.ok(r.counts.machine_seconds > 0 && r.counts.system_wait_seconds >= 0, `${task.id}: machine seconds`);
      // Within one run the waits are part of the clock (a baseline of several repeats takes the
      // median of each separately, so only a single run is held to this).
      if (!b.repeats) assert.ok(r.counts.system_wait_seconds <= r.counts.machine_seconds + 0.001, `${task.id}: waits exceed the clock`);
      // Machine seconds run from the first step to the verified end on screen: the clock stops
      // right after the last step or wait (round 2: the done screenshot moved the start of the
      // clock, so machine seconds came out shorter than the run, even negative).
      assert.ok(Array.isArray(r.waits), `${task.id} ${r.id ?? ''}: the baseline records no waits; re-capture it`);
      const ends = [...r.steps.map(x => x.at + x.took), ...r.waits.map(w => w.at + w.seconds)];
      const lastEnd = Math.max(0, ...ends);
      const own = r.counts.machine_seconds;
      // A variant record is one execution; a top-level record of an odd number of repeats is the
      // median run itself (an even number takes the mean of the middle two, so only the bound below holds).
      if (b.variants || !b.repeats || b.repeats % 2 === 1) {
        assert.ok(own + 0.002 >= lastEnd, `${task.id} ${r.id ?? ''}: machine ${own}s ends before its last step or wait (${round(lastEnd)}s)`);
        assert.ok(own - lastEnd <= 0.5, `${task.id} ${r.id ?? ''}: machine ${own}s runs ${round(own - lastEnd)}s past its last step or wait`);
      }
    }
    // Over repeats each median is taken separately; a median of waits never exceeds the median of
    // the clocks they were part of.
    assert.ok(b.counts.system_wait_seconds <= b.counts.machine_seconds + 0.002, `${task.id}: system wait ${b.counts.system_wait_seconds}s exceeds machine ${b.counts.machine_seconds}s`);
    assert.ok(b.counts.machine_seconds > 0, `${task.id}: machine seconds must be positive`);
    // The result counts, per metric, the best verified expert path.
    for (const m of ['steps', 'keystrokes', 'human_seconds']) {
      assert.equal(b.counts[m], Math.min(...runs.map(r => r.counts[m])), `${task.id}: ${m} is not the best path's`);
    }
    assert.equal(b.counts.human_plus_wait_seconds, round(b.counts.human_plus_wait_seconds), `${task.id}: rounding`);
    for (const m of METRICS) assert.equal(typeof b.counts[m], 'number', `${task.id}: ${m}`);
    // Round 10: a task that saves was saved by the measured part, in its declared end state.
    if (task.saves) {
      const spec = endStateOf(task, 'odoo');
      for (const r of runs.length > 1 ? runs : [b]) {
        const ss = r.saved_state || b.saved_state;
        assert.ok(ss && Object.keys(ss.end_state_changed || {}).length, `${task.id} ${r.id ?? ''}: the baseline records no change in the task's end state`);
        assert.ok((ss.writes_sent || []).some(w => { const [m, a] = w.split(' '); return spec.writes.some(x => matchesRequest(x, m, a)); }),
          `${task.id} ${r.id ?? ''}: the baseline's measured part sent none of the task's declared writes (${spec.writes.join(', ')})`);
      }
    }
  });
}
