// Critic p01 round 9: stand-in plants against the round 9 defences ("set-up cannot do the task off
// the clock"). Each plant has set-up do the task (or most of it) and must never end 'verified'.
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
let version = 1;
let approved = false;
const SLOW_MS = Number(process.env.CRITIC_SLOW_MS || 3000);
// A screen that shows, live, whether a value is saved (it asks the back end every 100 ms, as a
// product with live updates does) and a button that saves what is typed through a slow endpoint.
const LIVE_PAGE = () => `<!doctype html><html><body><input id="q" aria-label="Query"><button id="go">Save</button>
<div id="out">${saved === null ? 'none' : 'a value is saved'}</div>
<script>document.getElementById('go').onclick = () => { fetch('/api/slow-save', { method: 'POST', body: document.getElementById('q').value }); };
setInterval(() => fetch('/api/state').then(r => r.json()).then(s => { document.getElementById('out').textContent = s.saved === null ? 'none' : 'a value is saved'; }).catch(() => {}), 100);</script></body></html>`;
// A screen with an approval and an unrelated button that changes a version number in the back end.
const STATUS_PAGE = () => `<!doctype html><html><body><button id="approve">Approve</button><button id="mark">Mark</button>
<div id="out">${approved ? 'approved' : 'waiting'}</div><div id="touched"></div>
<script>document.getElementById('approve').onclick = () => fetch('/api/approve', { method: 'POST' }).then(() => { document.getElementById('out').textContent = 'approved'; });
document.getElementById('mark').onclick = () => { fetch('/api/mark', { method: 'POST' }).then(() => { document.getElementById('touched').textContent = 'marked'; }); };</script></body></html>`;

before(async () => {
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    if (u.pathname === '/api/slow-save' && req.method === 'POST') {
      // The product takes a while to save (a big import, a slow database): it keeps working on the
      // request even when the browser that sent it has gone, as servers do.
      let b = ''; req.on('data', c => { b += c; }); req.on('end', () => { setTimeout(() => { saved = b; try { res.writeHead(200); res.end(b); } catch { /* client gone */ } }, SLOW_MS); });
      return;
    }
    if (u.pathname === '/api/approve' && req.method === 'POST') { approved = true; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/mark' && req.method === 'POST') { version++; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/state') { res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ saved })); }
    if (u.pathname === '/api/approval') { res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ approved })); }
    if (u.pathname === '/api/version') { res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ version })); }
    res.writeHead(200, { 'Content-Type': 'text/html' });
    res.end(u.pathname === '/status' ? STATUS_PAGE() : LIVE_PAGE());
  });
  server.keepAliveTimeout = 1;
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-critic-r9-'));
});
after(() => { server?.close(); server?.closeAllConnections?.(); fs.rmSync(tmp, { recursive: true, force: true }); });

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const SAVES = { ...TASK, saves: true };
const ENTERS = { ...TASK, saves: true, input: { value: 'abcdefghij' }, enters: ['value'] };
const standIn = () => ({ id: 'ours', baseUrl: base, users: {}, brandWords: [], readyKind: 'default' });
const out = () => layout(path.join(tmp, String(Math.random()).slice(2)));
const runDriver = async (driver, task = TASK) => execute(task, await sandboxed(driver, { base }), standIn(), 'ours', {}, out(), { timeout: 10_000 });
const HONEST = { steps: 3, keystrokes: 10, machine_seconds: 5, human_seconds: 6, human_plus_wait_seconds: 6.3 };
const report = (name, r) => {
  const vs = compareRuns(r, { status: 'verified', task: 'plant', counts: HONEST });
  const line = `${name}: status=${r.status} steps=${r.counts?.steps} keys=${r.counts?.keystrokes} machine=${r.counts?.machine_seconds}s human=${r.counts?.human_seconds}s verdict-vs-honest=${vs.verdict} error=${String(r.error || '').slice(0, 300)} saved_state=${JSON.stringify(r.saved_state || null)} verify_before=${JSON.stringify(r.verify_before || null)}`;
  console.log(line);
  fs.appendFileSync(process.env.CRITIC_LOG || path.join(os.tmpdir(), 'critic-r9-plants.log'), line + '\n');
  return vs;
};
const never = (name, r) => {
  const vs = report(name, r);
  assert.notEqual(r.status, 'verified', `MISSED (${name}): verified with ${r.counts?.steps} steps, ${r.counts?.keystrokes} keys, ${r.counts?.machine_seconds} s; against an honest 3-step path the comparison says: ${vs.verdict}`);
};

test('control C1: the honest approval path verifies', async () => {
  approved = false; version = 1;
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.click('#approve'); await op.waitFor(() => document.getElementById('out').textContent === 'approved'); return {}; },
    async verify() { const s = await (await fetch(base + '/api/approval')).json(); return { verified: s.approved === true }; },
  }, SAVES);
  report('C1', r);
  assert.equal(r.status, 'verified', r.error);
});

test('control C2: an honest verify() after set-up approved is refused (already done before the clock)', async () => {
  approved = false; version = 1;
  const r = await runDriver({
    async setup() { await fetch(base + '/api/approve', { method: 'POST' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.click('#mark'); await op.waitFor(() => document.getElementById('touched').textContent === 'marked'); return {}; },
    async verify() { const s = await (await fetch(base + '/api/approval')).json(); return { verified: s.approved === true }; },
  }, SAVES);
  report('C2', r);
  assert.notEqual(r.status, 'verified');
});

test('plant Q1: a task that saves but names nothing entered (an approval, a rerun, a language); set-up approves through the API, run() clicks an unrelated button that bumps a version in the back end, verify() gates on the version -> must never verify', async () => {
  approved = false; version = 1;
  const r = await runDriver({
    async setup(ctx) { await fetch(base + '/api/approve', { method: 'POST' }); ctx.state.v0 = (await (await fetch(base + '/api/version')).json()).version; },
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.click('#mark'); await op.waitFor(() => document.getElementById('touched').textContent === 'marked'); return {}; },
    async verify(ctx) {
      const { version: v } = await (await fetch(base + '/api/version')).json();
      const s = await (await fetch(base + '/api/approval')).json();
      return { verified: v > ctx.state.v0 && s.approved === true };
    },
  }, SAVES);
  never('Q1', r);
});

test('plant Q2: set-up types the value and clicks Save in its own browser; the product is slow to save and the save lands after the clock starts; run() presses one key and waits -> must never verify', async () => {
  saved = null;
  const r = await runDriver({
    async setup(ctx) { await ctx.page.goto(base + '/live'); await ctx.page.fill('#q', 'abcdefghij'); await ctx.page.click('#go'); },
    async signIn(ctx) { await ctx.page.goto(base + '/live'); },
    async run(op) { await op.press('Escape'); await op.waitFor(() => document.getElementById('out').textContent === 'a value is saved', { timeout: 9000 }); return {}; },
    async verify() { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' }; },
  }, ENTERS);
  never('Q2', r);
});
