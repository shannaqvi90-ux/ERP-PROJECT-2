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
import { METRICS, driverFingerprint } from '../lib/runner.mjs';

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
    assert.deepEqual(b.driver, driverFingerprint('odoo', task.id),
      `${task.id}: the driver changed after its baseline was taken; run node run.mjs --task ${task.id} --product odoo --repeat 3`);

    const runs = b.variants ? b.variants : [{ id: null, steps: b.steps, counts: b.counts, status: b.status }];
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
    }
    // The result counts, per metric, the best verified expert path.
    for (const m of ['steps', 'keystrokes', 'human_seconds']) {
      assert.equal(b.counts[m], Math.min(...runs.map(r => r.counts[m])), `${task.id}: ${m} is not the best path's`);
    }
    assert.equal(b.counts.human_plus_wait_seconds, round(b.counts.human_plus_wait_seconds), `${task.id}: rounding`);
    for (const m of METRICS) assert.equal(typeof b.counts[m], 'number', `${task.id}: ${m}`);
  });
}
