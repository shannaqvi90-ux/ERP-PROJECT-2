// The bar only moves up (CLAUDE.md rule 9): gauntlet/ratchet.json records minimum counts for the
// comparison harness, and this suite fails when any count falls below its minimum.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { BASELINE_DIR, HARNESS_DIR, REPO_ROOT } from '../lib/config.mjs';
import { loadDriver, loadTasks } from '../lib/registry.mjs';
import { MUTATIONS, tapResult, unionPattern } from '../scripts/mutations.mjs';

const ratchet = JSON.parse(fs.readFileSync(path.join(REPO_ROOT, 'gauntlet', 'ratchet.json'), 'utf8'));
const KEYS = { tasks: 'compare.tasks', named_tasks: 'compare.namedTasks', odoo_drivers_built: 'compare.odooDriversBuilt',
  odoo_baselines_verified: 'compare.odooBaselinesVerified', reference_main_lists: 'compare.referenceMainLists',
  reference_rows_per_main_list: 'compare.referenceRowsPerMainList', harness_tests: 'compare.harnessTests', live_tests: 'compare.liveTests', ours_drivers_built: 'compare.oursDriversBuilt',
  guard_plants: 'compare.guardPlants', api_tasks: 'compare.apiTasks', page_function_plants: 'compare.pageFunctionPlants',
  instrument_mutations: 'compare.instrumentMutations', before_clock_plants: 'compare.beforeClockPlants', saves_tasks: 'compare.savesTasks',
  enters_tasks: 'compare.entersTasks' };
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

test('built drivers for our product never go below their minimum', async () => {
  let built = 0;
  for (const t of await loadTasks()) if ((await loadDriver('ours', t.id)).built !== false) built++;
  assert.ok(built >= min.ours_drivers_built, `${built} < ${min.ours_drivers_built}`);
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

// Tests that run in ./erp verify (every file but the live rig checks) count toward
// compare.harnessTests; the live rig checks (two plus one per task) toward compare.liveTests.
// Tests generated per task (a loop of `test(` calls) count once per task.
async function countTests(file, tasks) {
  const text = fs.readFileSync(file, 'utf8');
  const top = (text.match(/^test\(/gm) || []).length;
  const perTask = /^for \(const t(ask)? of await loadTasks\(\)\) \{\n\s+test\(/m.test(text) ? tasks : 0;
  return top + perTask;
}

test('harness tests never go below their minimum (live rig checks counted apart)', async () => {
  const dir = path.join(HARNESS_DIR, 'test');
  const tasks = (await loadTasks()).length;
  let n = 0;
  let live = 0;
  for (const f of fs.readdirSync(dir).filter(x => x.endsWith('.test.mjs'))) {
    const c = await countTests(path.join(dir, f), tasks);
    if (f === 'live-odoo.test.mjs') live += c; else n += c;
  }
  assert.ok(n >= min.harness_tests, `${n} tests that run in ./erp verify < ${min.harness_tests}`);
  assert.ok(live >= min.live_tests, `${live} live rig tests < ${min.live_tests}`);
});

test('planted uncounted-action drivers never go below their minimum', () => {
  // test/guard.test.mjs: one run per entry of PLANTS, plus each top-level test named "plant…".
  const text = fs.readFileSync(path.join(HARNESS_DIR, 'test', 'guard.test.mjs'), 'utf8');
  const table = text.slice(text.indexOf('const PLANTS = {'), text.indexOf('\n};', text.indexOf('const PLANTS = {')));
  const entries = (table.match(/^ {2}'[^']+': async/gm) || []).length;
  const named = (text.match(/^test\((['"`])plant/gm) || []).length;
  assert.ok(entries + named >= min.guard_plants, `${entries + named} plants < ${min.guard_plants}`);
});

test('page-function plants never go below their minimum (round 7)', () => {
  // test/page-script.test.mjs: the functions the source check refuses (REFUSED), the actions the
  // read world refuses on its own (ACTS) and the plants run end to end (PLANTS), one each.
  const text = fs.readFileSync(path.join(HARNESS_DIR, 'test', 'page-script.test.mjs'), 'utf8');
  const entries = name => {
    const start = text.indexOf(`const ${name} = {`);
    assert.ok(start >= 0, `${name} missing from test/page-script.test.mjs`);
    const table = text.slice(start, text.indexOf('\n};', start));
    return (table.match(/^ {2}'(?:[^'\\]|\\.)+': /gm) || []).length;
  };
  const n = entries('REFUSED') + entries('ACTS') + entries('PLANTS');
  assert.ok(n >= min.page_function_plants, `${n} page-function plants < ${min.page_function_plants}`);
});

// Round 9 (critic p01 r8): set-up cannot do the task off the clock. The plants that try it, and the
// tasks whose saved end state the harness judges, never go below their minimum.
test('before-the-clock plants, tasks that save and tasks that name what the person enters never go below their minimum', async () => {
  const text = fs.readFileSync(path.join(HARNESS_DIR, 'test', 'before-clock.test.mjs'), 'utf8');
  const plants = (text.match(/^test\('plant P/gm) || []).length;
  assert.ok(plants >= min.before_clock_plants, `${plants} before-the-clock plants < ${min.before_clock_plants}`);
  const tasks = await loadTasks();
  assert.ok(tasks.filter(t => t.saves).length >= min.saves_tasks, `tasks that save < ${min.saves_tasks}`);
  assert.ok(tasks.filter(t => t.enters?.length).length >= min.enters_tasks, `tasks that name what the person enters < ${min.enters_tasks}`);
});

test('API tasks never go below their minimum', async () => {
  const n = (await loadTasks()).filter(t => t.channel === 'api').length;
  assert.ok(n >= min.api_tasks, `${n} API tasks < ${min.api_tasks}`);
});

// The instrument's mutation check (scripts/mutations.mjs) runs in ./erp verify: every mutation
// must fail a self-test. Here: never fewer mutations than the minimum, and each still finds the
// text it mutates (a defence rewritten without updating its mutation would otherwise pass unseen).
test('instrument mutations never go below their minimum, and each still finds what it mutates', () => {
  assert.ok(MUTATIONS.length >= min.instrument_mutations, `${MUTATIONS.length} instrument mutations < ${min.instrument_mutations}`);
  assert.equal(new Set(MUTATIONS.map(m => m[0])).size, MUTATIONS.length, 'mutation ids are unique');
  for (const [id, , file, text, replacement, testFile] of MUTATIONS) {
    assert.notEqual(text, replacement, `${id}: changes nothing`);
    assert.ok(fs.readFileSync(path.join(HARNESS_DIR, file), 'utf8').includes(text), `${id}: ${file} no longer holds the text it mutates`);
    assert.ok(fs.existsSync(path.join(HARNESS_DIR, testFile)), `${id}: ${testFile} missing`);
  }
});

// Round 8: the controls of one test file run together, once, and each test is judged by name.
test('the mutation check reads each test by name, and runs one control per test file for all its patterns', () => {
  assert.deepEqual(tapResult('ok 3 - the freeze holds'), { passed: true, name: 'the freeze holds' });
  assert.deepEqual(tapResult('    not ok 1 - plant T3 (round 6): never verified'), { passed: false, name: 'plant T3 (round 6): never verified' });
  assert.deepEqual(tapResult('not ok 2 - flaky # TODO later'), { passed: false, name: 'flaky' });
  assert.equal(tapResult('ok 4 - filtered out # SKIP test name does not match pattern'), null);
  assert.equal(tapResult('# Subtest: the freeze holds'), null);
  assert.equal(unionPattern(['live ticker|plant T3', 'plant T6']), '(?:live ticker|plant T3)|(?:plant T6)');
  assert.equal(unionPattern(['plant T6', '']), '', 'a whole-file pattern makes the control the whole file');
  // Every mutation's own tests are inside its file's control.
  for (const [id, , , , , testFile, pattern] of MUTATIONS) {
    const union = new RegExp(unionPattern(MUTATIONS.filter(m => m[5] === testFile).map(m => m[6])));
    if (pattern) assert.ok(union.source.includes(`(?:${pattern})`) || union.source === '(?:)', id);
  }
});
