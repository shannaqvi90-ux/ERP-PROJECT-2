import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import { HARNESS_DIR } from '../lib/config.mjs';
import { writeReview } from '../lib/review.mjs';
import { compareRuns } from '../lib/runner.mjs';

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

test('one command runs a task on our product and writes a result JSON', () => {
  const out = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-cli-'));
  try {
    const r = spawnSync(process.execPath, [RUN, '--task', 'switch-to-arabic', '--product', 'ours', '--out', out], { encoding: 'utf8' });
    assert.equal(r.status, 0, r.stderr);
    const files = fs.readdirSync(path.join(out, 'results'));
    assert.equal(files.length, 1);
    const res = JSON.parse(fs.readFileSync(path.join(out, 'results', files[0]), 'utf8'));
    assert.equal(res.task, 'switch-to-arabic');
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
