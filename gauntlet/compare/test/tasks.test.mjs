import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { PRODUCT_IDS, describe, driverPath, loadDriver, loadTasks } from '../lib/registry.mjs';
import { runTask } from '../lib/runner.mjs';
import { loadNeedles } from '../data/generate.mjs';

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

test('every task has a driver for each product', async () => {
  for (const t of await loadTasks()) {
    for (const p of PRODUCT_IDS) {
      assert.ok(fs.existsSync(driverPath(p, t.id)), `${p} driver for ${t.id}`);
      const d = await loadDriver(p, t.id);
      assert.equal(typeof d.run, 'function');
      if (d.built !== false) assert.ok(d.path, `${p}/${t.id}: describe the expert path in "path"`);
      for (const [id, v] of Object.entries(d.variants || {})) {
        assert.equal(typeof v.run, 'function', `${p}/${t.id} variant ${id}: run(op, ctx)`);
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
