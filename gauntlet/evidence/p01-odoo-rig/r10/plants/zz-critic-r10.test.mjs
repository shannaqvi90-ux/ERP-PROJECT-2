// Critic p01 round 10: plants against the round-10 saved-state defences (set-up off the clock).
// Each plant is a stand-in product plus a driver; the instrument must never record a plant as verified.
//   C1  control: the honest typed save verifies.
//   C2  control: the honest approval verifies.
//   R1  set-up stashes the value in the product (a server-side draft, an API write before the clock);
//       the start page offers "Save draft"; run() clicks it once. The page's write carries the value,
//       so the value counts as "entered by the measured part" though the person typed nothing.
//   R2  set-up turns on a product setting through the API; the start page itself sends the declared
//       approval write on a timer a moment after it opens. run() presses Escape and waits.
//   R3  R1 and R2 together: set-up stashes the value, the start page autosaves it on a timer; run()
//       presses Escape and waits: one key, the value never typed.
//   R4  set-up stashes the value; the page's Save sends whatever the stash holds, the measured part
//       types a different, short text into an unrelated search box first (to look busy) - not needed;
//       kept out. (R1-R3 suffice.)
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout, compareRuns } from '../lib/runner.mjs';
import { loadNeedles } from '../data/generate.mjs';
import { sandboxed } from './helpers/driver-module.mjs';

let server, base, tmp, needles;
let saved = null;
let stash = null;
let approved = false;
let autoApprove = false;

const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
// A form with a typed save (honest path), and a "draft restored" banner when the product holds a draft.
const DRAFT_PAGE = auto => `<!doctype html><html><body><input id="q" aria-label="Query"><button id="go">Save</button>
${stash !== null ? `<div id="banner">Draft restored: <span>${esc(stash)}</span> <button id="sd">Save draft</button></div>` : ''}<div id="out"></div>
<script>const q = document.getElementById('q'); const out = document.getElementById('out');
const save = v => fetch('/api/save', { method: 'POST', body: v }).then(() => { out.textContent = 'saved'; });
document.getElementById('go').onclick = () => save(q.value);
const sd = document.getElementById('sd'); if (sd) sd.onclick = () => fetch('/api/draft').then(r => r.text()).then(save);
${auto ? "if (sd) setTimeout(() => fetch('/api/draft').then(r => r.text()).then(save), 3000);" : ''}
</script></body></html>`;
const STATUS_PAGE = () => `<!doctype html><html><body><button id="approve">Approve</button><div id="out">${approved ? 'approved' : 'waiting'}</div>
<script>const out = document.getElementById('out');
const approve = () => fetch('/api/approve', { method: 'POST' }).then(() => { out.textContent = 'approved'; });
document.getElementById('approve').onclick = approve;
${autoApprove ? 'setTimeout(approve, 3000);' : ''}
</script></body></html>`;

before(async () => {
  needles = loadNeedles();
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    const json = (v, status = 200) => { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(v)); };
    const body = fn => { let b = ''; req.on('data', c => { b += c; }); req.on('end', () => fn(b)); };
    if (u.pathname === '/api/save' && req.method === 'POST') return body(b => { saved = b; stash = null; res.writeHead(200); res.end(b); });
    if (u.pathname === '/api/draft' && req.method === 'POST') return body(b => { stash = b; res.writeHead(204); res.end(); });
    if (u.pathname === '/api/draft') { res.writeHead(200, { 'Content-Type': 'text/plain' }); return res.end(stash ?? ''); }
    if (u.pathname === '/api/slow-save' && req.method === 'POST') return body(b => setTimeout(() => { saved = b; try { res.writeHead(200); res.end(b); } catch { /* gone */ } }, 1500));
    if (u.pathname === '/api/state') return json({ saved });
    if (u.pathname === '/api/approve' && req.method === 'POST') { approved = true; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/settings' && req.method === 'POST') { autoApprove = true; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/approval') return json({ approved });
    res.writeHead(200, { 'Content-Type': 'text/html' });
    if (u.pathname.startsWith('/abort')) return res.end(`<!doctype html><html><body><input id="q" aria-label="Query"><button id="go">Save</button><div id="out">${saved === null ? 'none' : 'a value is saved'}</div>
<script>document.getElementById('go').onclick = () => { const c = new AbortController(); fetch('/api/slow-save', { method: 'POST', body: document.getElementById('q').value, signal: c.signal }).catch(() => {}); setTimeout(() => c.abort(), 200); };
setInterval(() => fetch('/api/state').then(r => r.json()).then(s => { document.getElementById('out').textContent = s.saved === null ? 'none' : 'a value is saved'; }).catch(() => {}), 100);</script></body></html>`);
    res.end(u.pathname.startsWith('/status') ? STATUS_PAGE() : DRAFT_PAGE(u.pathname.startsWith('/auto')));
  });
  server.keepAliveTimeout = 1;
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-critic-r10-'));
});
after(() => { server?.close(); server?.closeAllConnections?.(); fs.rmSync(tmp, { recursive: true, force: true }); });

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const ENTERS = { ...TASK, saves: true, input: { value: 'abcdefghij' }, enters: ['value'],
  endState: { ours: { reads: [{ read: 'GET /api/state', parts: ['saved'] }], writes: ['POST /api/save'] } } };
const SAVES = { ...TASK, saves: true, endState: { ours: { reads: [{ read: 'GET /api/approval', parts: ['approved'] }], writes: ['POST /api/approve'] } } };
const standIn = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'admin', password: 'x' } }, brandWords: [], readyKind: 'default' });
const out = () => layout(path.join(tmp, String(Math.random()).slice(2)));
const runDriver = async (driver, task) => execute(task, await sandboxed(driver, { base }), standIn(), 'ours', needles, out(), { timeout: 10_000 });
const reset = () => { saved = null; stash = null; approved = false; autoApprove = false; };
const HONEST = { steps: 3, keystrokes: 10, machine_seconds: 5, human_seconds: 6, human_plus_wait_seconds: 6.3 };
const report = (name, r) => {
  const vs = r.status === 'verified' ? compareRuns(r, { status: 'verified', task: 'plant', counts: HONEST }) : null;
  console.log(`RESULT ${name}: status=${r.status} steps=${r.counts?.steps} keys=${r.counts?.keystrokes} machine=${r.counts?.machine_seconds} human=${r.counts?.human_seconds} vsHonest=${vs?.verdict ?? '-'} writes_sent=${JSON.stringify(r.saved_state?.writes_sent)} gained=${JSON.stringify(r.saved_state?.end_state_gained)} error=${(r.error || '').slice(0, 300)}`);
  return vs;
};
const never = (name, r) => {
  const vs = report(name, r);
  assert.notEqual(r.status, 'verified', `MISSED (${name}): verified with ${r.counts?.steps} steps, ${r.counts?.keystrokes} keys, ${r.counts?.machine_seconds} s; against an honest path (3 steps, 10 keys) the comparison says: ${vs?.verdict}`);
};
const verifySaved = async () => ({ verified: (await (await fetch(base + '/api/state')).json()).saved === 'abcdefghij' });
const verifyApproval = async () => ({ verified: (await (await fetch(base + '/api/approval')).json()).approved === true });

test('control C1: the honest typed save verifies', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/form'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'saved'); return {}; },
    verify: verifySaved,
  }, ENTERS);
  report('C1', r);
  assert.equal(r.status, 'verified', r.error);
});

test('control C2: the honest approval verifies', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.click('#approve'); await op.waitFor(() => document.getElementById('out').textContent === 'approved'); return {}; },
    verify: verifyApproval,
  }, SAVES);
  report('C2', r);
  assert.equal(r.status, 'verified', r.error);
});

test('plant R1: set-up stashes the value as a server-side draft; run() clicks "Save draft" once, typing nothing -> must never verify', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/draft', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/form'); },
    async run(op) { await op.click('#sd'); await op.waitFor(() => document.getElementById('out').textContent === 'saved'); return {}; },
    verify: verifySaved,
  }, ENTERS);
  never('R1', r);
});

test('plant R2: set-up turns on a setting; the start page sends the declared approval write on its own timer; run() presses Escape -> must never verify', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/settings', { method: 'POST' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.press('Escape'); await op.waitFor(() => document.getElementById('out').textContent === 'approved', { timeout: 9000 }); return {}; },
    verify: verifyApproval,
  }, SAVES);
  never('R2', r);
});

test('plant R3: set-up stashes the value; the start page autosaves it on a timer; run() presses Escape -> must never verify', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/draft', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/auto'); },
    async run(op) { await op.press('Escape'); await op.waitFor(() => document.getElementById('out').textContent === 'saved', { timeout: 9000 }); return {}; },
    verify: verifySaved,
  }, { ...ENTERS, startAt: 'list' });
  never('R3', r);
});

test('plant R5: set-up types the value and clicks a Save whose request the page aborts after 200 ms (the product still carries it out 1.5 s later); run() presses Escape -> must never verify', async () => {
  reset();
  const r = await runDriver({
    async setup(ctx) { await ctx.page.goto(base + '/abort'); await ctx.page.fill('#q', 'abcdefghij'); await ctx.page.click('#go'); await new Promise(r => setTimeout(r, 400)); },
    async signIn(ctx) { await ctx.page.goto(base + '/abort'); },
    async run(op) { await op.press('Escape'); await op.waitFor(() => document.getElementById('out').textContent === 'a value is saved', { timeout: 9000 }); return {}; },
    verify: verifySaved,
  }, { ...ENTERS, endState: { ours: { reads: [{ read: 'GET /api/state', parts: ['saved'] }] } } });
  never('R5', r);
});
