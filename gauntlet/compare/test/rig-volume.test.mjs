// The bar's volume rule on the live rig (lib/rig-volume.mjs): Odoo vacuums job-run rows older than
// a week, so a rig seeded once falls short of 100,000 rows about a week later (it held 86,561 on
// 2026-10-07). An Odoo run against a short rig is refused, never recorded.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { HARNESS_DIR } from '../lib/config.mjs';
import { checkRigVolume, describeShort, MAIN_LISTS, MIN_ROWS_PER_MAIN_LIST, TOP_UP_HINT } from '../lib/rig-volume.mjs';

const RUN = path.join(HARNESS_DIR, 'run.mjs');

function fakeRpc(shortModels = []) {
  const calls = [];
  return {
    calls,
    async call(model, method, args, kwargs) {
      calls.push({ model, method, args, kwargs });
      return shortModels.includes(model) ? [] : [kwargs.offset + 1];
    },
  };
}

test('rig volume: every main list is asked for its 100,000th row', async () => {
  const rpc = fakeRpc();
  const r = await checkRigVolume(rpc);
  assert.equal(r.ok, true);
  assert.deepEqual(r.short, []);
  assert.equal(MIN_ROWS_PER_MAIN_LIST, 100_000);
  assert.equal(rpc.calls.length, 7);
  assert.deepEqual(rpc.calls.map(c => c.model).sort(), Object.values(MAIN_LISTS).map(([m]) => m).sort());
  for (const c of rpc.calls) {
    assert.equal(c.method, 'search');
    assert.equal(c.kwargs.offset, 99_999);
    assert.equal(c.kwargs.limit, 1);
    assert.equal(c.kwargs.context.active_test, false, 'archived rows count as rows, as up.sh counts them');
  }
  for (const name of ['contacts', 'users', 'currency_rates', 'audit_messages', 'attachments', 'job_runs', 'approvals']) assert.ok(MAIN_LISTS[name], name);
});

test('rig volume: a list one row short is reported by name with the top-up command', async () => {
  const r = await checkRigVolume(fakeRpc(['ir.cron.progress']));
  assert.equal(r.ok, false);
  assert.deepEqual(r.short, ['job_runs']);
  assert.match(describeShort(r), /job_runs \(ir\.cron\.progress\) hold fewer than 100,000 rows/);
  assert.match(TOP_UP_HINT, /tools\/odoo-reference\/up\.sh/);
});

/** A stand-in Odoo that signs in and answers search with no row for the given models. */
async function fakeOdoo(shortModels) {
  const server = http.createServer((req, res) => {
    let body = '';
    req.on('data', c => { body += c; });
    req.on('end', () => {
      const { id, params } = JSON.parse(body || '{}');
      let result = null;
      if (req.url === '/web/session/authenticate') result = { uid: 2 };
      else if (req.url.startsWith('/web/dataset/call_kw/')) result = shortModels.includes(params.model) ? [] : [params.kwargs.offset + 1];
      res.writeHead(200, { 'Content-Type': 'application/json', 'Set-Cookie': 'session_id=fake; Path=/' });
      res.end(JSON.stringify({ jsonrpc: '2.0', id, result }));
    });
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  return { server, url: `http://127.0.0.1:${server.address().port}` };
}

function runCli(args, env, stopWhen = null) {
  return new Promise(resolve => {
    const child = spawn(process.execPath, [RUN, ...args], { env: { ...process.env, ...env }, stdio: ['ignore', 'pipe', 'pipe'] });
    let stdout = '', stderr = '';
    child.stdout.on('data', d => { stdout += d; if (stopWhen && stopWhen.test(stdout)) child.kill('SIGTERM'); });
    child.stderr.on('data', d => { stderr += d; });
    child.on('close', status => resolve({ status, stdout, stderr }));
  });
}

test('run.mjs refuses an Odoo run when the live rig is short of a main list, and records nothing', async () => {
  const { server, url } = await fakeOdoo(['ir.cron.progress']);
  const out = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-rig-short-'));
  try {
    for (const product of ['odoo', 'both']) {
      const r = await runCli(['--task', 'find-user', '--product', product, '--out', out], { COMPARE_ODOO_URL: url });
      assert.equal(r.status, 2, r.stdout + r.stderr);
      assert.match(r.stderr, /short of the bar: job_runs \(ir\.cron\.progress\)/);
      assert.match(r.stderr, /up\.sh/);
      assert.equal(fs.existsSync(path.join(out, 'results')), false, 'no result is written for a short rig');
    }
  } finally {
    server.close();
    fs.rmSync(out, { recursive: true, force: true });
  }
});

test('run.mjs refuses an Odoo run when the rig cannot be checked', async () => {
  const { server, url } = await fakeOdoo([]);
  await new Promise(r => server.close(r));
  const out = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-rig-down-'));
  try {
    const r = await runCli(['--task', 'find-user', '--product', 'odoo', '--out', out], { COMPARE_ODOO_URL: url });
    assert.equal(r.status, 2, r.stdout + r.stderr);
    assert.match(r.stderr, /could not be checked/);
    assert.equal(fs.existsSync(path.join(out, 'results')), false);
  } finally { fs.rmSync(out, { recursive: true, force: true }); }
});

test('run.mjs checks the rig before an Odoo run and goes on when every list holds 100,000 rows', async () => {
  // The stand-in passes the volume check; the run is stopped as soon as the CLI reports it (the
  // stand-in is not a web client, so the task itself is not run here).
  const { server, url } = await fakeOdoo([]);
  const out = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-rig-ok-'));
  try {
    const r = await runCli(['--task', 'sign-in', '--product', 'odoo', '--out', out], { COMPARE_ODOO_URL: url }, /checked live/);
    assert.match(r.stdout, /reference rig: at least 100,000 rows in each of 7 main lists \(checked live\)/);
    assert.doesNotMatch(r.stderr, /short of the bar|could not be checked/);
  } finally {
    server.close();
    fs.rmSync(out, { recursive: true, force: true });
  }
});
