import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { PRODUCT_IDS, describe, driverPath, loadDriver, loadTasks } from '../lib/registry.mjs';
import { endStateOf, endStateProblems, runTask } from '../lib/runner.mjs';
import { START_KINDS } from '../lib/start.mjs';
import { loadNeedles } from '../data/generate.mjs';
import { HARNESS_DIR } from '../lib/config.mjs';

// The six tasks the owner's bar names. Tasks are never removed (plan.md).
export const NAMED = ['find-record', 'create-restricted-user', 'custom-field-filter', 'switch-to-arabic', 'import-5000', 'follow-approval'];

test('every task the bar names is defined and marked as named', async () => {
  const tasks = await loadTasks();
  for (const id of NAMED) {
    const t = tasks.find(x => x.id === id);
    assert.ok(t, `task ${id} missing`);
    assert.equal(t.named, true, `${id} must be marked named`);
  }
});

test('task definitions are product-neutral and complete', async () => {
  for (const t of await loadTasks()) {
    for (const k of ['id', 'title', 'actor', 'start', 'goal', 'done']) assert.ok(t[k], `${t.id}: ${k} missing`);
    const text = [t.title, t.goal, t.done, t.start].join(' ');
    assert.doesNotMatch(text, /odoo/i, `${t.id}: the goal, start and done text must not name a product`);
  }
});

test('every task declares where the runner starts it and the moments every driver shoots (instrument 4)', async () => {
  for (const t of await loadTasks()) {
    assert.ok(START_KINDS.includes(t.startAt), `${t.id}: startAt must be one of ${START_KINDS.join(', ')}`);
    if (t.channel === 'api') assert.equal(t.startAt, 'api', `${t.id}: an API task starts in the API`);
    if (t.startAt === 'sign-in') assert.match(t.start, /signed out/i, `${t.id}: a sign-in start is signed out`);
    if (t.startAt === 'home') assert.match(t.start, /right after sign-in/i, `${t.id}: a home start is the screen after sign-in`);
    assert.ok(Array.isArray(t.moments), `${t.id}: moments (possibly empty)`);
    assert.equal(new Set(t.moments).size, t.moments.length, `${t.id}: each moment once`);
    for (const m of t.moments) assert.doesNotMatch(m, /odoo|^start$|^done$|^error$/i, `${t.id}: moment "${m}" must be neutral and not a runner moment`);
    // Every built driver shoots exactly the declared moments (statically: the runner checks it at run time too).
    for (const p of PRODUCT_IDS) {
      const d = await loadDriver(p, t.id);
      if (d.built === false) continue;
      const src = fs.readFileSync(driverPath(p, t.id), 'utf8');
      const shot = [...src.matchAll(/\.shot\((['"`])([^'"`]+)\1\)/g)].map(x => x[2]);
      assert.deepEqual([...new Set(shot)].sort(), [...t.moments].sort(), `${p}/${t.id}: shoots ${JSON.stringify(shot)} but the task declares ${JSON.stringify(t.moments)}`);
    }
  }
});

test('every task has a driver for each product', async () => {
  for (const t of await loadTasks()) {
    for (const p of PRODUCT_IDS) {
      assert.ok(fs.existsSync(driverPath(p, t.id)), `${p} driver for ${t.id}`);
      // Described by the driver process (lib/sandbox/): the harness never imports a driver.
      const d = await loadDriver(p, t.id);
      assert.equal(d.hooks.run, true, `${p}/${t.id}: run(op, ctx) must be a function`);
      if (d.built !== false) assert.ok(d.path, `${p}/${t.id}: describe the expert path in "path"`);
      for (const [id, v] of Object.entries(d.variants || {})) {
        assert.equal(v.run, true, `${p}/${t.id} variant ${id}: run(op, ctx)`);
        assert.ok(v.path, `${p}/${t.id} variant ${id}: describe the expert path in "path"`);
      }
    }
  }
});

test('every Odoo driver is built; placeholders in task text resolve from the dataset', async () => {
  const needles = loadNeedles();
  for (const t of await loadTasks()) {
    assert.notEqual((await loadDriver('odoo', t.id)).built, false, `odoo/${t.id} must be built`);
    const values = { ...needles, ...(t.input || {}) };
    assert.doesNotMatch(describe(t.goal, values) + describe(t.done, values) + describe(t.start, values), /\{[a-z_.]+\}/i, `${t.id}: unresolved placeholder`);
  }
});

test('an unbuilt driver reports "not built" without touching a browser', async () => {
  const out = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-nb-'));
  try {
    const d = await loadDriver('ours', 'find-record');
    if (d.built !== false) return; // filled in by a later piece
    const r = await runTask('find-record', 'ours', { outDir: out });
    assert.equal(r.status, 'not_built');
    assert.match(r.error, /not built/);
    const file = path.join(out, 'results', `${r.run_id}.json`);
    assert.ok(fs.existsSync(file));
    assert.equal(JSON.parse(fs.readFileSync(file, 'utf8')).status, 'not_built');
  } finally { fs.rmSync(out, { recursive: true, force: true }); }
});

test('print-list-arabic (routed from p06): the ours driver takes its menu words from the product\'s own strings, which hold them in both languages', async () => {
  const task = (await loadTasks()).find(t => t.id === 'print-list-arabic');
  assert.ok(task, 'the task exists');
  assert.equal(task.startAt, 'list');
  assert.deepEqual(task.moments, ['list filtered']);
  const source = fs.readFileSync(path.join(HARNESS_DIR, 'drivers', 'ours', 'print-list-arabic.mjs'), 'utf8');
  // No Arabic word is written into the driver: a renamed or retranslated menu fails here, not in a run.
  assert.doesNotMatch(source, /[؀-ۿ]/, 'the driver repeats an Arabic word instead of reading it from the product');
  const keys = [...source.matchAll(/STRINGS\['([^']+)'\]/g)].map(m => m[1]);
  assert.deepEqual([...new Set(keys)].sort(), ['lists.print.open', 'lists.print.pdfArabic']);
  const strings = lang => JSON.parse(fs.readFileSync(path.join(HARNESS_DIR, '..', '..', 'web', 'src', 'modules', 'lists', 'i18n', `${lang}.json`), 'utf8'));
  const ar = strings('ar');
  const en = strings('en');
  for (const k of keys) {
    assert.match(ar[k] ?? '', /[؀-ۿ]/, `${k} has no Arabic text in the product's Arabic strings`);
    assert.ok((en[k] ?? '').trim(), `${k} has no English text in the product's English strings`);
    assert.notEqual(ar[k], en[k], `${k} is not translated`);
  }
});

// Round 9 (critic p01 r8): what the harness judges about the end state is declared by the task.
test('every task that ends in a state saved in the product says so (saves), and names what the person enters', async () => {
  for (const t of await loadTasks()) {
    for (const k of ['saves', 'keyboardOnly']) if (k in t) assert.equal(typeof t[k], 'boolean', `${t.id}: ${k} is true or false`);
    // A task whose done text names the back end is judged on what verify() reads from it.
    if (/back end/i.test(t.done)) assert.equal(t.saves, true, `${t.id}: its end state is in the back end ("${t.done}"), so it declares saves: true`);
    if (/no step used the mouse/i.test(t.done)) assert.equal(t.keyboardOnly, true, `${t.id}: keyboard only, judged by the harness`);
    if (t.enters !== undefined) {
      assert.ok(Array.isArray(t.enters) && t.enters.length > 0, `${t.id}: enters lists input keys`);
      assert.equal(t.saves, true, `${t.id}: a task that names what the person enters saves it`);
      for (const k of t.enters) {
        assert.ok(t.input && k in t.input, `${t.id}: enters names input.${k}, which the task does not define`);
        assert.ok(String(t.input[k]).trim().length >= 4, `${t.id}: input.${k} is long enough to be found again ("${t.input[k]}")`);
      }
    }
    // Every built driver reads its end state with verify() (run() never reports it, round 9).
    for (const p of PRODUCT_IDS) {
      const d = await loadDriver(p, t.id);
      if (d.built !== false) assert.ok(d.hooks.verify || Object.values(d.variants || {}).every(v => v.hooks?.verify), `${p}/${t.id}: no verify()`);
    }
    // Every built driver of a task that saves reads its end state (verify), in both products.
    if (t.saves) {
      for (const p of PRODUCT_IDS) {
        const d = await loadDriver(p, t.id);
        if (d.built !== false) assert.equal(d.hooks.verify, true, `${p}/${t.id}: a task that saves is verified from the back end by verify()`);
      }
    }
  }
});

// Round 10 (critic p01 r9, plant Q1): a task that saves declares where its end state lives in each
// product whose driver is built: the back-end read and the parts of its answer. The harness accepts
// only a change there as the saved state (lib/runner.mjs, savedState); a driver cannot declare it.
test('every task that saves declares its end state (the read and the parts that hold it) for each product whose driver is built', async () => {
  let declared = 0;
  for (const t of await loadTasks()) {
    if (!t.saves) { assert.equal(t.endState, undefined, `${t.id}: only a task that saves declares an end state`); continue; }
    assert.deepEqual(endStateProblems(t.endState), [], `${t.id}: endState is malformed`);
    for (const p of Object.keys(t.endState)) assert.ok(PRODUCT_IDS.includes(p), `${t.id}: endState names an unknown product ${p}`);
    for (const p of PRODUCT_IDS) {
      const d = await loadDriver(p, t.id);
      if (d.built === false) continue;
      assert.ok(endStateOf(t, p), `${t.id}: the ${p} driver is built, so the task declares its end state for ${p} (endState.${p})`);
      assert.ok(endStateOf(t, p).writes, `${t.id}: the ${p} driver is built, so the task names the writes that save its end state (endState.${p}.writes)`);
      declared++;
    }
  }
  assert.ok(declared >= 13 + 6, `only ${declared} built drivers of tasks that save have a declared end state`);
});
