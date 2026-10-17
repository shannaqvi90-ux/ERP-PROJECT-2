// Critic p01 round 12 plants (fresh critic). Stand-in product, same scaffolding as
// test/causation.test.mjs. Each plant must never be recorded as 'verified'.
//
// The round-12 instrument judges a task "of no traceable origin" (a message) as caused when the page
// task just before it was caused by a step and no uncaused page task ran in between. Every in-page
// way of timing the write (timer, poll, port, worker, frame loop) leaves an uncaused page task right
// before the message, which the guard sees. These plants keep the timing OUT of the page's thread:
//
//   W1  WebSocket push: set-up turns on a setting. The stand-in, armed by verify()'s read before the
//       clock, pushes "show" over a web socket the page opened at load (the page shows a Close
//       button), then pushes "go" 500 ms later on its OWN server-side timer, whatever the person
//       does. The page approves on "go". run() = one click on Close. The click does not cause "go"
//       (it would arrive without it), but the message task follows the click's input task with no
//       page activity between them.
//   W2  The same with the server's two pushes replaced by a cross-site iframe (another renderer
//       process): the iframe polls the stand-in and posts "show" and, on its own 500 ms timer, "go"
//       to the parent. Its timers run outside the page's main thread.
//   W3  The near-limit delay (R2b/S2/S10 shape within the tolerances): one click on plain text asks
//       GET /api/tick (held 1.9 s, under LINK_SECONDS 2), then the page waits a further 0.9 s on a
//       single-shot timer (under REACTION_SECONDS 1) and approves: a 2.8 s delay armed by a click
//       on plain text.
//   C1  control: the honest approval click verifies.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import crypto from 'node:crypto';
import { execute, layout, compareRuns } from '../lib/runner.mjs';
import { loadNeedles } from '../data/generate.mjs';
import { sandboxed } from './helpers/driver-module.mjs';

let server, base, frameBase, tmp, needles;
let approved = false;
let autoApprove = false;
let armedAt = null;
const sockets = new Set();
const timers = new Set();
const later = (ms, fn) => { const t = setTimeout(() => { timers.delete(t); fn(); }, ms); timers.add(t); };

// One unmasked text frame from server to client.
const frame = text => { const b = Buffer.from(text); return Buffer.concat([Buffer.from([0x81, b.length]), b]); };
const push = text => { for (const s of sockets) { try { s.write(frame(text)); } catch { /* gone */ } } };

const PAGE = mode => `<!doctype html><html><body><button id="approve">Approve</button><div id="out">${approved ? 'approved' : 'waiting'}</div>
<script>const out = document.getElementById('out');
const banner = () => { document.body.insertAdjacentHTML('beforeend', '<div id="banner">Notice <button id="close">Close</button></div>'); document.getElementById('close').onclick = () => {}; };
const approve = () => fetch('/api/approve', { method: 'POST' }).then(() => { out.textContent = 'approved'; });
document.getElementById('approve').onclick = approve;
${autoApprove && mode === 'ws' ? "const ws = new WebSocket(location.origin.replace('http', 'ws') + '/ws'); ws.onmessage = e => { if (e.data === 'show') banner(); if (e.data === 'go') approve(); };" : ''}
${autoApprove && mode === 'iframe' ? `addEventListener('message', e => { if (e.data === 'show') banner(); if (e.data === 'go') approve(); }); document.body.insertAdjacentHTML('beforeend', '<iframe src="${'FRAMEBASE'}/frame" style="width:10px;height:10px;border:0"></iframe>');` : ''}
${autoApprove && mode === 'near' ? "document.getElementById('out').addEventListener('pointerdown', () => fetch('/api/tick19').then(() => setTimeout(approve, 900)), { once: true });" : ''}
</script></body></html>`;

const FRAME = `<!doctype html><html><body><script>
const poll = setInterval(() => fetch('/api/phase').then(r => r.json()).then(j => { if (j.sign) { clearInterval(poll); parent.postMessage('show', '*'); setTimeout(() => parent.postMessage('go', '*'), 500); } }).catch(() => {}), 50);
</script></body></html>`;

before(async () => {
  needles = loadNeedles();
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    const json = (v, status = 200) => { res.writeHead(status, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' }); res.end(JSON.stringify(v)); };
    if (u.pathname === '/api/approve' && req.method === 'POST') { approved = true; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/settings' && req.method === 'POST') { autoApprove = true; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/approval') {
      if (autoApprove && armedAt === null) {
        armedAt = Date.now();
        // W1: the stand-in's own timer, never the person's: "show" a while into the clock (it starts
        // 1.1-2.1 s after this read), then "go" 500 ms later.
        later(2300, () => push('show'));
        later(2800, () => push('go'));
      }
      return json({ approved });
    }
    if (u.pathname === '/api/phase') return json({ sign: armedAt !== null && Date.now() - armedAt >= 2300 });
    if (u.pathname === '/api/tick19') return later(1900, () => json({ ok: true }));
    if (u.pathname === '/frame') { res.writeHead(200, { 'Content-Type': 'text/html' }); return res.end(FRAME); }
    res.writeHead(200, { 'Content-Type': 'text/html' });
    return res.end(PAGE(u.pathname.split('-')[1] || 'plain').replace('FRAMEBASE', frameBase));
  });
  server.on('upgrade', (req, socket) => {
    const key = req.headers['sec-websocket-key'];
    const accept = crypto.createHash('sha1').update(key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
    socket.write(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\n\r\n`);
    sockets.add(socket);
    socket.on('data', () => {});
    socket.on('error', () => sockets.delete(socket));
    socket.on('close', () => sockets.delete(socket));
  });
  server.keepAliveTimeout = 1;
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  // Another site (localhost, not 127.0.0.1): the iframe gets a renderer process of its own.
  frameBase = `http://localhost:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-critic-r12-'));
});
after(() => {
  for (const t of timers) clearTimeout(t);
  for (const s of sockets) s.destroy();
  server?.close(); server?.closeAllConnections?.();
  fs.rmSync(tmp, { recursive: true, force: true });
});

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const SAVES = { ...TASK, saves: true, endState: { ours: { reads: [{ read: 'GET /api/approval', parts: ['approved'] }], writes: ['POST /api/approve'] } } };
const standIn = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'admin', password: 'x' } }, brandWords: [], readyKind: 'default' });
const out = () => layout(path.join(tmp, String(Math.random()).slice(2)));
const runDriver = async (driver, task = SAVES) => execute(task, await sandboxed(driver, { base }), standIn(), 'ours', needles, out(), { timeout: 10_000 });
const reset = () => { for (const t of timers) clearTimeout(t); timers.clear(); approved = false; autoApprove = false; armedAt = null; };
// Odoo's recorded follow-approval baseline (gauntlet/reference/odoo/tasks/follow-approval.json).
const ODOO_FOLLOW_APPROVAL = { steps: 4, keystrokes: 0, machine_seconds: 3.521, human_seconds: 10.6, human_plus_wait_seconds: 10.6 };
const verifyApproval = async () => ({ verified: (await (await fetch(base + '/api/approval')).json()).approved === true });
const report = (name, r) => {
  const vs = r.status === 'verified' ? compareRuns(r, { status: 'verified', task: 'plant', counts: ODOO_FOLLOW_APPROVAL }) : null;
  console.log(`RESULT ${name}: status=${r.status} steps=${r.counts?.steps} keys=${r.counts?.keystrokes} machine=${r.counts?.machine_seconds} human=${r.counts?.human_seconds} writes_caused=${JSON.stringify(r.saved_state?.writes_caused)} vs_odoo_follow_approval=${vs?.verdict} error=${r.error ?? ''}`);
  assert.notEqual(r.status, 'verified', `MISSED (${name}): verified with ${r.counts?.steps} steps, ${r.counts?.keystrokes} keys, ${r.counts?.machine_seconds} s; against Odoo's follow-approval baseline the comparison says ${vs?.verdict}`);
};
const plant = (mode, run) => ({
  async setup() { await fetch(base + '/api/settings', { method: 'POST' }); },
  signIn: new Function('ctx', `return ctx.page.goto(base + '/status-${mode}');`), // eslint-disable-line no-new-func
  run,
  verify: verifyApproval,
});

test('critic r12 control C1: the honest approval click verifies', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/status-plain'); },
    async run(op) { await op.click('#approve'); await op.waitFor(() => document.getElementById('out').textContent === 'approved'); return {}; },
    verify: verifyApproval,
  });
  console.log(`RESULT C1: status=${r.status} steps=${r.counts?.steps} writes_caused=${JSON.stringify(r.saved_state?.writes_caused)} error=${r.error ?? ''}`);
  assert.equal(r.status, 'verified', r.error);
});

test('critic r12 plant W1: the stand-in pushes "go" over a web socket on its own timer; one click on a Close that appeared just before takes the credit -> never verifies', async () => {
  reset();
  report('W1', await runDriver(plant('ws', async op => { await op.click('#close'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; })));
});

test('critic r12 plant W2: a cross-site iframe posts "go" to the page on its own timer; one click on Close takes the credit -> never verifies', async () => {
  reset();
  report('W2', await runDriver(plant('iframe', async op => { await op.click('#close'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; })));
});

test('critic r12 plant W3: one click on plain text arms a 2.8 s delay (1.9 s held answer + 0.9 s timer) before the approval -> never verifies', async () => {
  reset();
  report('W3', await runDriver(plant('near', async op => { await op.click('#out'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; })));
});
