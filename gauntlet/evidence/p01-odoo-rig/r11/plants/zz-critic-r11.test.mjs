// Critic p01 round 11 plants (fresh critic). Stand-in product, same scaffolding as
// test/measured-part.test.mjs. Each plant must never be recorded as 'verified'.
//
//   S1  set-up turns on a setting; the start page sends the declared approval on its own timer
//       (round 10's R2) and then shows a "Close" banner. run() makes ONE click on "Close": the click
//       step waits for its target to appear, so the timer's write goes out "while a counted step was
//       being performed" and is credited to the click.
//   S1b the same with a scroll step (op.scrollTo waits for its target too).
//   S2  R2b (a timer the first key arms, 2.5 s) with the timer replaced by a slow back-end read (and
//       a click on plain text instead of the key): the first pointer press makes the page ask GET /api/tick, the stand-in answers after 2.5 s, the page approves on
//       the answer. The "one link" rule credits it to the key.
//   S3  no step involved in the page's timing at all: the stand-in arms itself when verify()'s
//       before-the-clock read arrives (the runner's last act before the clock), and answers the
//       page's ordinary poll with "go" ~0.6 s later; the page approves. run() clicks plain text once.
//   C1  control: the honest approval click verifies.
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
let approved = false;
let autoApprove = false;
let armedAt = null;
const timers = new Set();
const later = (ms, fn) => { const t = setTimeout(() => { timers.delete(t); fn(); }, ms); timers.add(t); };

const PAGE = mode => `<!doctype html><html><body><button id="approve">Approve</button><div id="out">${approved ? 'approved' : 'waiting'}</div><div id="spacer" style="height:3000px"></div>
<script>const out = document.getElementById('out');
const banner = () => { document.body.insertAdjacentHTML('beforeend', '<div id="banner">Approved <button id="close">Close</button></div>'); document.getElementById('close').onclick = () => {}; };
const approve = () => fetch('/api/approve', { method: 'POST' }).then(() => { out.textContent = 'approved'; banner(); });
document.getElementById('approve').onclick = approve;
${autoApprove && mode === 'timer' ? 'setTimeout(approve, 3000);' : ''}
${autoApprove && mode === 'held' ? "addEventListener('pointerdown', () => fetch('/api/tick').then(approve), { once: true });" : ''}
${autoApprove && mode === 'poll' ? "let done = false; setInterval(() => fetch('/api/go').then(r => r.json()).then(j => { if (j.go && !done) { done = true; approve(); } }).catch(() => {}), 50);" : ''}
</script></body></html>`;

before(async () => {
  needles = loadNeedles();
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    const json = (v, status = 200) => { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(v)); };
    if (u.pathname === '/api/approve' && req.method === 'POST') { approved = true; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/settings' && req.method === 'POST') { autoApprove = true; res.writeHead(204); return res.end(); }
    // verify()'s read; the first one after the setting (the before-the-clock call) arms the stand-in (S3).
    if (u.pathname === '/api/approval') { if (autoApprove && armedAt === null) armedAt = Date.now(); return json({ approved }); }
    if (u.pathname === '/api/go') return json({ go: armedAt !== null && Date.now() - armedAt >= Number(process.env.S3_DELAY_MS || 600) });
    if (u.pathname === '/api/tick') return later(2500, () => json({ ok: true }));
    res.writeHead(200, { 'Content-Type': 'text/html' });
    return res.end(PAGE(u.pathname.split('-')[1] || 'timer'));
  });
  server.keepAliveTimeout = 1;
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-critic-r11-'));
});
after(() => {
  for (const t of timers) clearTimeout(t);
  server?.close(); server?.closeAllConnections?.();
  fs.rmSync(tmp, { recursive: true, force: true });
});

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const SAVES = { ...TASK, saves: true, endState: { ours: { reads: [{ read: 'GET /api/approval', parts: ['approved'] }], writes: ['POST /api/approve'] } } };
const standIn = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'admin', password: 'x' } }, brandWords: [], readyKind: 'default' });
const out = () => layout(path.join(tmp, String(Math.random()).slice(2)));
const runDriver = async (driver, task) => execute(task, await sandboxed(driver, { base }), standIn(), 'ours', needles, out(), { timeout: 10_000 });
const reset = () => { for (const t of timers) clearTimeout(t); timers.clear(); approved = false; autoApprove = false; armedAt = null; };
// Odoo's recorded follow-approval baseline (gauntlet/reference/odoo/tasks/follow-approval.json).
const ODOO_FOLLOW_APPROVAL = { steps: 4, keystrokes: 0, machine_seconds: 3.521, human_seconds: 10.6, human_plus_wait_seconds: 10.6 };
const verifyApproval = async () => ({ verified: (await (await fetch(base + '/api/approval')).json()).approved === true });
const report = (name, r) => {
  const vs = r.status === 'verified' ? compareRuns(r, { status: 'verified', task: 'plant', counts: ODOO_FOLLOW_APPROVAL }) : null;
  console.log(`RESULT ${name}: status=${r.status} steps=${r.counts?.steps} keys=${r.counts?.keystrokes} machine=${r.counts?.machine_seconds} human=${r.counts?.human_seconds} writes_caused=${JSON.stringify(r.saved_state?.writes_caused)} vs_odoo_follow_approval=${vs?.verdict} error=${r.error ?? ''}`);
  assert.notEqual(r.status, 'verified', `MISSED (${name}): verified with ${r.counts?.steps} steps, ${r.counts?.keystrokes} keys, ${r.counts?.machine_seconds} s; against Odoo's follow-approval baseline the comparison says ${vs?.verdict}`);
};

test('critic r11 control C1: the honest approval click verifies', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/status-timer'); },
    async run(op) { await op.click('#approve'); await op.waitFor(() => document.getElementById('out').textContent === 'approved'); return {}; },
    verify: verifyApproval,
  }, SAVES);
  console.log(`RESULT C1: status=${r.status} steps=${r.counts?.steps} error=${r.error ?? ''}`);
  assert.equal(r.status, 'verified', r.error);
});

test('critic r11 plant S1: the page approves on its own timer; one click on a "Close" that appears afterwards takes the credit -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/settings', { method: 'POST' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/status-timer'); },
    async run(op) { await op.click('#close'); return {}; },
    verify: verifyApproval,
  }, SAVES);
  report('S1', r);
});

test('critic r11 plant S1b: the same with one scroll step to the banner -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/settings', { method: 'POST' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/status-timer'); },
    async run(op) { await op.scrollTo('#close'); return {}; },
    verify: verifyApproval,
  }, SAVES);
  report('S1b', r);
});

test('critic r11 plant S2: R2b with the 2.5 s timer moved into a slow back-end answer (the one link) -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/settings', { method: 'POST' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/status-held'); },
    async run(op) { await op.click('#out'); await op.waitFor(() => document.getElementById('out').textContent === 'approved', { timeout: 9000 }); return {}; },
    verify: verifyApproval,
  }, SAVES);
  report('S2', r);
});

test('critic r11 plant S3: the stand-in arms on verify()\'s before-the-clock read and lets the page\'s poll approve; one click on plain text -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/settings', { method: 'POST' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/status-poll'); },
    async run(op) { await op.click('#out'); await op.waitFor(() => document.getElementById('out').textContent === 'approved', { timeout: 9000 }); return {}; },
    verify: verifyApproval,
  }, SAVES);
  report('S3', r);
});
