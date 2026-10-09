// A run that ends in an error keeps something to look at (routed to p01: a health-check failure in
// ./erp verify once left only "locator.waitFor: Timeout 120000ms exceeded"). The result records
// the page's address and its last console lines and page errors; an error before the measured part
// (no operator, so no 'error' shot) also leaves a plain screenshot in <out>/failures/, never in the
// reviewer's blind/ folder and never in the reference folder.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout } from '../lib/runner.mjs';
import { REPO_ROOT } from '../lib/config.mjs';
import { sandboxed } from './helpers/driver-module.mjs';

const PAGE = `<!doctype html><html><body><input id="q" aria-label="Query"><div id="out"></div>
  <script>console.log('stand-in says hello'); console.error('stand-in failed to load its menu');
  setTimeout(() => { throw new Error('stand-in page error'); }, 0);</script></body></html>`;
let server, base, tmp;
before(async () => {
  server = http.createServer((req, res) => { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(PAGE); });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-failure-capture-'));
});
after(() => { server?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const product = () => ({ id: 'ours', baseUrl: base, users: {}, brandWords: [], readyKind: 'default' });
// A sign-in that never reaches its working screen: the run errors before the measured part.
const stuckSignIn = {
  async signIn(ctx) {
    await ctx.page.goto(base + '/plant');
    await ctx.until(() => document.getElementById('out').textContent === 'working', { timeout: 1_500 });
  },
  async run(op) { await op.click('#q'); return {}; },
  async verify() { return { verified: true }; },
};

test('an error before the measured part keeps the address, the console and a screenshot outside blind/', async () => {
  const out = layout(path.join(tmp, 'side-by-side'));
  const r = await execute(TASK, await sandboxed(stuckSignIn, { base }), product(), 'ours', {}, out, { timeout: 5_000 });
  assert.equal(r.status, 'error', r.error);
  assert.equal(r.counts, null, 'nothing was measured');
  const c = r.failure_capture;
  assert.ok(c, 'failure_capture recorded');
  assert.equal(c.url, `${base}/plant`);
  assert.ok(c.console.includes('log: stand-in says hello'), c.console.join('\n'));
  assert.ok(c.console.includes('error: stand-in failed to load its menu'), c.console.join('\n'));
  assert.ok(c.console.some(l => /^pageerror: stand-in page error/.test(l)), c.console.join('\n'));
  assert.match(c.screenshot, /failures\/failure-[0-9a-f]{16}\.jpg$/);
  const file = path.join(REPO_ROOT, c.screenshot);
  assert.ok(fs.existsSync(file) && fs.statSync(file).size > 1000, 'the screenshot was written');
  assert.ok(!c.screenshot.includes('/blind/'), 'never in the reviewer folder');
  assert.equal(fs.existsSync(path.join(out.outDir, 'blind', 'shots')) ? fs.readdirSync(path.join(out.outDir, 'blind', 'shots')).length : 0, 0);
});

test('an error in the reference folder records the address and console but writes no screenshot there', async () => {
  const out = layout(path.join(tmp, 'reference'), { baseline: true });
  const r = await execute(TASK, await sandboxed(stuckSignIn, { base }), product(), 'ours', {}, out, { timeout: 5_000 });
  assert.equal(r.status, 'error', r.error);
  assert.equal(r.failure_capture.url, `${base}/plant`);
  assert.ok(r.failure_capture.console.length >= 2);
  assert.equal(r.failure_capture.screenshot, undefined);
  assert.equal(fs.existsSync(path.join(out.outDir, 'failures')), false);
});

test('a verified run records no failure capture', async () => {
  const out = layout(path.join(tmp, 'verified'));
  const r = await execute(TASK, await sandboxed({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.click('#q'); return {}; },
    async verify(ctx, outcome) { return { verified: outcome !== undefined }; },
  }, { base }), product(), 'ours', {}, out, { timeout: 10_000 });
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.failure_capture, undefined);
});
