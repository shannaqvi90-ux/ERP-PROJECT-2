// The bar only moves up (CLAUDE.md rule 9): gauntlet/ratchet.json records minimum counts for the
// comparison harness, and this suite fails when any count falls below its minimum.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { BASELINE_DIR, HARNESS_DIR, REPO_ROOT } from '../lib/config.mjs';
import { loadDriver, loadTasks } from '../lib/registry.mjs';

const ratchet = JSON.parse(fs.readFileSync(path.join(REPO_ROOT, 'gauntlet', 'ratchet.json'), 'utf8'));
const KEYS = { tasks: 'compare.tasks', named_tasks: 'compare.namedTasks', odoo_drivers_built: 'compare.odooDriversBuilt',
  odoo_baselines_verified: 'compare.odooBaselinesVerified', reference_main_lists: 'compare.referenceMainLists',
  reference_rows_per_main_list: 'compare.referenceRowsPerMainList', harness_tests: 'compare.harnessTests' };
const min = Object.fromEntries(Object.entries(KEYS).map(([k, key]) => [k, ratchet.minimums?.[key]]));

test('ratchet.json has every comparison minimum', () => {
  for (const [k, key] of Object.entries(KEYS)) assert.equal(typeof min[k], 'number', `gauntlet/ratchet.json: minimums["${key}"] missing`);
});

test('tasks and named tasks never go below their minimum', async () => {
  const tasks = await loadTasks();
  assert.ok(tasks.length >= min.tasks, `${tasks.length} tasks < ${min.tasks}`);
  assert.ok(tasks.filter(t => t.named).length >= min.named_tasks);
});

test('built Odoo drivers never go below their minimum', async () => {
  let built = 0;
  for (const t of await loadTasks()) if ((await loadDriver('odoo', t.id)).built !== false) built++;
  assert.ok(built >= min.odoo_drivers_built, `${built} < ${min.odoo_drivers_built}`);
});

test('verified Odoo baselines never go below their minimum, and each is complete', () => {
  const dir = path.join(BASELINE_DIR, 'tasks');
  const files = fs.existsSync(dir) ? fs.readdirSync(dir).filter(f => f.endsWith('.json')) : [];
  const verified = files.map(f => JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8'))).filter(r => r.status === 'verified');
  assert.ok(verified.length >= min.odoo_baselines_verified, `${verified.length} verified baselines < ${min.odoo_baselines_verified}`);
  for (const r of verified) {
    assert.equal(r.product, 'odoo');
    for (const k of ['steps', 'keystrokes', 'machine_seconds', 'human_seconds']) assert.equal(typeof r.counts[k], 'number', `${r.task}: ${k}`);
    assert.ok(r.screenshots.length >= 2, `${r.task}: screenshots`);
    for (const s of r.screenshots) assert.ok(fs.existsSync(path.join(REPO_ROOT, s.path)), `${r.task}: ${s.path} missing`);
  }
});

test('the recorded reference volume never goes below the minimum per main list', () => {
  const v = JSON.parse(fs.readFileSync(path.join(BASELINE_DIR, 'volume.json'), 'utf8'));
  assert.ok(v.main_lists.length >= min.reference_main_lists, `${v.main_lists.length} main lists < ${min.reference_main_lists}`);
  for (const k of v.main_lists) assert.ok(v.lists[k].count >= min.reference_rows_per_main_list, `${k}: ${v.lists[k].count}`);
});

test('harness tests never go below their minimum', () => {
  const dir = path.join(HARNESS_DIR, 'test');
  const n = fs.readdirSync(dir).filter(f => f.endsWith('.test.mjs'))
    .reduce((s, f) => s + (fs.readFileSync(path.join(dir, f), 'utf8').match(/^test\(/gm) || []).length, 0);
  assert.ok(n >= min.harness_tests, `${n} tests < ${min.harness_tests}`);
});
