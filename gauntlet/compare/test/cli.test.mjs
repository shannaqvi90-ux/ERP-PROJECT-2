import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import { HARNESS_DIR } from '../lib/config.mjs';
import { writeReview } from '../lib/review.mjs';
import { compareRuns } from '../lib/runner.mjs';
import { loadDriver, loadTasks } from '../lib/registry.mjs';

const RUN = path.join(HARNESS_DIR, 'run.mjs');

test('--list prints every task', () => {
  const out = execFileSync(process.execPath, [RUN, '--list'], { encoding: 'utf8' });
  for (const id of ['find-record', 'create-restricted-user', 'custom-field-filter', 'switch-to-arabic', 'import-5000', 'follow-approval']) assert.match(out, new RegExp(id));
});

test('bad arguments fail with usage', () => {
  const r = spawnSync(process.execPath, [RUN, '--bogus'], { encoding: 'utf8' });
  assert.notEqual(r.status, 0);
  const r2 = spawnSync(process.execPath, [RUN], { encoding: 'utf8' });
  assert.equal(r2.status, 2);
  assert.match(r2.stderr, /usage/);
});

test('one command runs a task on our product and writes a result JSON', async () => {
  // A task whose driver for our product is still a stub runs without a product (exit 0, "not
  // built"). Built drivers need the product running, which the unit suite does not start.
  let task = null;
  for (const t of await loadTasks()) if ((await loadDriver('ours', t.id)).built === false) { task = t.id; break; }
  assert.ok(task, 'no unbuilt driver left: point this test at a running product instead');
  const out = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-cli-'));
  try {
    const r = spawnSync(process.execPath, [RUN, '--task', task, '--product', 'ours', '--out', out], { encoding: 'utf8' });
    assert.equal(r.status, 0, r.stderr);
    const files = fs.readdirSync(path.join(out, 'results'));
    assert.equal(files.length, 1);
    const res = JSON.parse(fs.readFileSync(path.join(out, 'results', files[0]), 'utf8'));
    assert.equal(res.task, task);
    assert.ok(['not_built', 'verified', 'failed', 'error'].includes(res.status));
  } finally { fs.rmSync(out, { recursive: true, force: true }); }
});

test('the blind review page shows A and B only and keeps the mapping in key.json', () => {
  const out = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-review-'));
  try {
    const mk = (product, file) => ({ product, task: 'find-record', status: 'verified', counts: { steps: 1, keystrokes: 1, machine_seconds: 1, human_seconds: 1, human_plus_wait_seconds: 1 }, screenshots: [{ moment: 'start', file }] });
    const runs = { ours: mk('ours', 'aaaaaaaaaaaaaaaa.jpg'), odoo: mk('odoo', 'bbbbbbbbbbbbbbbb.jpg') };
    const file = writeReview(out, [{ cmp: compareRuns(runs.ours, runs.odoo), runs }], () => 0.2);
    const html = fs.readFileSync(file, 'utf8');
    assert.doesNotMatch(html.replace(/key\.json/g, ''), /odoo|ours/i);
    assert.match(html, /<h3>A<\/h3>/);
    assert.match(html, /<h3>B<\/h3>/);
    const key = JSON.parse(fs.readFileSync(path.join(out, 'key.json'), 'utf8'));
    assert.deepEqual(key.letters['find-record'], { ours: 'A', odoo: 'B' });
  } finally { fs.rmSync(out, { recursive: true, force: true }); }
});

// Round 13 (critic p01 r12, mutation K9): the review page's letters are drawn at random per task by
// assignLetters, and each column shows the shots of the product its letter stands for in key.json.
test('the blind review page draws its letters at random per task and shows each product under its own letter', () => {
  const out = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-review-'));
  try {
    const mk = (product, task, file) => ({ product, task, status: 'verified', counts: { steps: 1, keystrokes: 1, machine_seconds: 1, human_seconds: 1, human_plus_wait_seconds: 1 }, screenshots: [{ moment: 'start', file }] });
    const pair = task => { const runs = { ours: mk('ours', task, `${task}-o.jpg`), odoo: mk('odoo', task, `${task}-r.jpg`) }; return { cmp: compareRuns(runs.ours, runs.odoo), runs }; };
    const column = (html, task, letter) => { const sec = html.split('<section>').find(x => x.includes(`<h2>${task}</h2>`)); return sec.split('<div class="col">').find(c => c.startsWith(`<h3>${letter}</h3>`)); };
    // A drawing that puts the reference first.
    let html = fs.readFileSync(writeReview(out, [pair('t1')], () => 0.9), 'utf8');
    let key = JSON.parse(fs.readFileSync(path.join(out, 'key.json'), 'utf8'));
    assert.deepEqual(key.letters.t1, { odoo: 'A', ours: 'B' });
    assert.match(column(html, 't1', 'A'), /t1-r\.jpg/);
    assert.match(column(html, 't1', 'B'), /t1-o\.jpg/);
    // Many tasks with the default drawing: both orders appear, and every column follows key.json.
    const tasks = Array.from({ length: 40 }, (_, i) => `task${i}`);
    html = fs.readFileSync(writeReview(out, tasks.map(pair)), 'utf8');
    key = JSON.parse(fs.readFileSync(path.join(out, 'key.json'), 'utf8'));
    const oursLetters = new Set(tasks.map(t => key.letters[t].ours));
    assert.deepEqual([...oursLetters].sort(), ['A', 'B'], 'our product got the same letter in all 40 tasks');
    for (const t of tasks) {
      assert.match(column(html, t, key.letters[t].ours), new RegExp(`${t}-o\\.jpg`));
      assert.match(column(html, t, key.letters[t].odoo), new RegExp(`${t}-r\\.jpg`));
    }
  } finally { fs.rmSync(out, { recursive: true, force: true }); }
});
