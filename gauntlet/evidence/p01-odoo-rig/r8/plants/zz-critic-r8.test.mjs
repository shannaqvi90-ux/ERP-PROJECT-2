// Critic r8 stand-in plants: a driver whose set-up does the task through the product's own API
// (allowed: set-up may act on the product) and whose verify() tells the before-clock call apart
// (it gets no outcome there) so the "already done before the clock" check never sees the end state.
// Copy to gauntlet/compare/test/ and run: node --test test/zz-critic-r8.test.mjs
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout, compareRuns } from '../lib/runner.mjs';
import { sandboxed } from './helpers/driver-module.mjs';

let server, base, tmp;
let saved = null;
let posts = 0;
const PAGE = () => `<!doctype html><html><body><input id="q" aria-label="Query"><button id="go">Save</button><div id="out">${saved === null ? 'none' : `saved ${saved}`}</div>
<script>document.getElementById('go').onclick = () => { const v = document.getElementById('q').value;
  fetch('/api/save', { method: 'POST', body: v }).then(r => r.text()).then(t => { document.getElementById('out').textContent = 'saved ' + t; }); };</script></body></html>`;

before(async () => {
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    if (u.pathname === '/api/save' && req.method === 'POST') { let b = ''; req.on('data', c => { b += c; }); req.on('end', () => { posts++; saved = b; res.writeHead(200); res.end(b); }); return; }
    if (u.pathname === '/api/state') { res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ saved })); }
    if (u.pathname === '/api/reset' && req.method === 'POST') { saved = null; res.writeHead(204); return res.end(); }
    res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(PAGE());
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'critic-r8-'));
});
after(() => { server?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const standIn = () => ({ id: 'ours', baseUrl: base, users: {}, brandWords: [], readyKind: 'default' });
const runDriver = async driver => execute(TASK, await sandboxed(driver, { base }), standIn(), 'ours', {}, layout(path.join(tmp, String(Math.random()).slice(2))), { timeout: 10_000 });
const reset = async () => { saved = null; };

test('control: the honest path verifies (3 steps, 10 keys)', async () => {
  await reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'saved abcdefghij', { label: 'saved' }); return { done: true }; },
    async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' }; },
  });
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.counts.steps, 3);
  assert.equal(r.counts.keystrokes, 10);
});

test('control: a driver whose set-up did the task and whose verify() is honest is refused (already done before the clock)', async () => {
  await reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run() { return { done: true }; },
    async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' }; },
  });
  assert.notEqual(r.status, 'verified', `verified with ${r.counts?.steps} steps`);
});

test('plant P1: set-up does the task through the API; verify() answers "not done" when it gets no outcome (before the clock); run() does nothing -> must never verify', async () => {
  await reset();
  const before = posts;
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run() { return { done: true }; },
    async verify(ctx, outcome) {
      if (outcome === undefined) return { verified: false };
      return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' };
    },
  });
  fs.writeFileSync(path.join(process.env.CRITIC_OUT || tmp, 'plant-P1-result.json'), JSON.stringify({ status: r.status, error: r.error, counts: r.counts, posts_in_setup: posts - before }, null, 2));
  const vs = compareRuns(r, { status: 'verified', task: 'plant', counts: { steps: 3, keystrokes: 10, machine_seconds: 0.5, human_seconds: 6, human_plus_wait_seconds: 6.3 } });
  assert.notEqual(r.status, 'verified', `MISSED: verified with ${r.counts?.steps} steps, ${r.counts?.keystrokes} keys, ${r.counts?.machine_seconds} s; against an honest 3-step path the comparison says: ${vs.verdict}`);
});

test('plant P1b: the same with verify() throwing when it gets no outcome (the runner reads a throw as "not done") -> must never verify', async () => {
  await reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.press('Escape'); return { done: true }; },
    async verify(ctx, outcome) {
      if (!outcome?.done) throw new Error('needs the outcome of run()');
      return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' };
    },
  });
  assert.notEqual(r.status, 'verified', `MISSED: verified with ${r.counts?.steps} steps, ${r.counts?.keystrokes} keys, ${r.counts?.machine_seconds} s`);
});

test('plant P2: set-up types the value through the API as a draft; the measured part only clicks Save -> must never verify on 1 step', async () => {
  await reset();
  // The stand-in keeps one value; set-up saves it, run() clicks Save once more (an empty field saves ""), so verify() reads the server state set in set-up.
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.waitFor('#out'); return { done: true }; },
    async verify(ctx, outcome) {
      if (outcome === undefined) return { verified: false };
      const s = await (await fetch(base + '/api/state')).json();
      return { verified: s.saved === 'abcdefghij' };
    },
  });
  assert.notEqual(r.status, 'verified', `MISSED: verified with ${r.counts?.steps} steps, ${r.counts?.keystrokes} keys, ${r.counts?.machine_seconds} s`);
});
