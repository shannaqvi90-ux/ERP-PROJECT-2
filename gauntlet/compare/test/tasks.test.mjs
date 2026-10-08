import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { PRODUCT_IDS, describe, driverPath, loadDriver, loadTasks } from '../lib/registry.mjs';
import { runTask } from '../lib/runner.mjs';
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
