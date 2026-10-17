// Round 11 (critic p01 r10, biggest gap): the instrument credited the person with whatever the page
// sent during the clock. Set-up could leave the task's work to the start page's own code:
//   R1  set-up stashes the value in the product (a server-side draft); the start page offers it as
//       text with a "Save draft" button; run() clicks once: the value rode in on the page's own write.
//   R2  set-up turns on a setting; the start page sends the declared approval write on a timer; run()
//       presses Escape and waits.
//   R3  R1 and R2 together: the start page autosaves the stashed value on a timer.
// The defences (lib/runner.mjs, measuredPartProblem and causedWrites): a write counts only when a
// counted step caused it (it went out during a step or within REACTION_SECONDS after it, or within
// that time of the answer to a request a step sent), and an entered value counts only when the
// measured part typed it or picked it as a file. Controls keep the honest shapes a product has: a save
// sent at once, a save sent a few event-loop turns later, a save sent once a pending check is
// answered (Odoo's onchange), and a save under way while the page changes its address.
// Also here (routed to p01 from p00 r10, p02 r10 and the critic's X1): set-up's watch on its own
// writes (watchSetUpBrowser) takes an address change for what it is, waits when set-up asks it to
// (ctx.settleWrites), names a write the browser aborted, and watches every address.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout, compareRuns, watchSetUpBrowser } from '../lib/runner.mjs';
import { launch } from '../lib/browser.mjs';
import { loadNeedles } from '../data/generate.mjs';
import { sandboxed } from './helpers/driver-module.mjs';

let server, base, tmp, needles;
let saved = null;
let stash = null;
let approved = false;
let autoApprove = false;
const timers = new Set();
const later = (ms, fn) => { const t = setTimeout(() => { timers.delete(t); fn(); }, ms); timers.add(t); };

const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
// A form with a typed save, and a "draft restored" banner when the product holds a draft (R1, R3).
const DRAFT_PAGE = auto => `<!doctype html><html><body><input id="q" aria-label="Query"><button id="go">Save</button>
${stash !== null ? `<div id="banner">Draft restored: <span>${esc(stash)}</span> <button id="sd">Save draft</button></div>` : ''}<div id="out"></div>
<script>const q = document.getElementById('q'); const out = document.getElementById('out');
const save = v => fetch('/api/save', { method: 'POST', body: v }).then(() => { out.textContent = 'saved'; });
document.getElementById('go').onclick = () => save(q.value);
const sd = document.getElementById('sd'); if (sd) sd.onclick = () => fetch('/api/draft').then(r => r.text()).then(save);
${auto ? "if (sd) setTimeout(() => fetch('/api/draft').then(r => r.text()).then(save), 3000);" : ''}
</script></body></html>`;
// An approval (C2); with the setting on, the page approves on its own timer (R2), or a while after the
// first key (R2b), polling the back end meanwhile (R2c).
const STATUS_PAGE = mode => `<!doctype html><html><body><button id="approve">Approve</button><div id="out">${approved ? 'approved' : 'waiting'}</div>
<script>const out = document.getElementById('out');
const approve = () => fetch('/api/approve', { method: 'POST' }).then(() => { out.textContent = 'approved'; });
document.getElementById('approve').onclick = approve;
${autoApprove && mode === 'timer' ? 'setTimeout(approve, 3000);' : ''}
${autoApprove && mode === 'key' ? "addEventListener('keydown', () => setTimeout(approve, 2500), { once: true });" : ''}
${autoApprove && mode === 'decoy' ? "document.body.insertAdjacentHTML('beforeend', '<button id=decoy>Approve another</button>'); document.getElementById('decoy').onclick = () => { fetch('/api/approve?id=other', { method: 'POST' }); setTimeout(approve, 2500); };" : ''}
${autoApprove && mode === 'poll' ? "setInterval(() => fetch('/api/approval').catch(() => {}), 100); addEventListener('keydown', () => setTimeout(approve, 2500), { once: true });" : ''}
</script></body></html>`;
// Honest shapes: the save is sent a few event-loop turns after the click (as a framework's scheduler
// does), or once a slow check the click sent first is answered (Odoo's pending onchange).
const SHAPES_PAGE = () => `<!doctype html><html><body><input id="q" aria-label="Query"><button id="later">Save later</button><button id="checked">Save checked</button><button id="moving">Save and move</button><div id="out"></div>
<script>const q = document.getElementById('q'); const out = document.getElementById('out');
const save = (path = '/api/save') => fetch(path, { method: 'POST', body: q.value }).then(() => { out.textContent = 'saved'; });
document.getElementById('later').onclick = () => { const c = new MessageChannel(); c.port1.onmessage = () => setTimeout(() => save(), 0); c.port2.postMessage(1); };
document.getElementById('checked').onclick = () => fetch('/api/check').then(() => save());
document.getElementById('moving').onclick = () => { save('/api/slow-save'); history.pushState({}, '', '/shapes/record'); };
</script></body></html>`;

before(async () => {
  needles = loadNeedles();
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    const json = (v, status = 200) => { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(v)); };
    const body = fn => { let b = ''; req.on('data', c => { b += c; }); req.on('end', () => fn(b)); };
    if (u.pathname === '/api/save' && req.method === 'POST') return body(b => { saved = b; stash = null; res.writeHead(200); res.end(b); });
    if (u.pathname === '/api/slow-save' && req.method === 'POST') return body(b => later(Number(u.searchParams.get('ms') || 800), () => { saved = b; try { res.writeHead(200); res.end(b); } catch { /* client gone */ } }));
    if (u.pathname === '/api/draft' && req.method === 'POST') return body(b => { stash = b; res.writeHead(204); res.end(); });
    if (u.pathname === '/api/draft') { res.writeHead(200, { 'Content-Type': 'text/plain' }); return res.end(stash ?? ''); }
    if (u.pathname === '/api/check') return later(1300, () => json({ ok: true }));
    if (u.pathname === '/api/state') return json({ saved });
    if (u.pathname === '/api/approve' && req.method === 'POST') { if (!u.searchParams.get('id')) approved = true; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/settings' && req.method === 'POST') { autoApprove = true; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/approval') return json({ approved });
    res.writeHead(200, { 'Content-Type': 'text/html' });
    if (u.pathname.startsWith('/status')) return res.end(STATUS_PAGE(u.pathname.split('-')[1] || 'timer'));
    if (u.pathname.startsWith('/shapes')) return res.end(SHAPES_PAGE());
    if (u.pathname.startsWith('/abort')) {
      return res.end(`<!doctype html><html><body><input id="q" aria-label="Query"><button id="go">Save</button>
<script>document.getElementById('go').onclick = () => { const c = new AbortController(); fetch('/api/slow-save?ms=1500', { method: 'POST', body: document.getElementById('q').value, signal: c.signal }).catch(() => {}); setTimeout(() => c.abort(), 200); };</script></body></html>`);
    }
    res.end(DRAFT_PAGE(u.pathname.startsWith('/auto')));
  });
  server.keepAliveTimeout = 1;
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-measured-part-'));
});
after(() => {
  for (const t of timers) clearTimeout(t);
  server?.close(); server?.closeAllConnections?.();
  fs.rmSync(tmp, { recursive: true, force: true });
});

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const ENTERS = { ...TASK, saves: true, input: { value: 'abcdefghij' }, enters: ['value'],
  endState: { ours: { reads: [{ read: 'GET /api/state', parts: ['saved'] }], writes: ['POST /api/save', 'POST /api/slow-save'] } } };
const SAVES = { ...TASK, saves: true, endState: { ours: { reads: [{ read: 'GET /api/approval', parts: ['approved'] }], writes: ['POST /api/approve'] } } };
const standIn = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'admin', password: 'x' } }, brandWords: [], readyKind: 'default' });
const out = () => layout(path.join(tmp, String(Math.random()).slice(2)));
const runDriver = async (driver, task) => execute(task, await sandboxed(driver, { base }), standIn(), 'ours', needles, out(), { timeout: 10_000 });
const reset = () => { for (const t of timers) clearTimeout(t); timers.clear(); saved = null; stash = null; approved = false; autoApprove = false; };
const HONEST = { steps: 3, keystrokes: 10, machine_seconds: 5, human_seconds: 6, human_plus_wait_seconds: 6.3 };
const never = (name, r) => {
  const vs = r.status === 'verified' ? compareRuns(r, { status: 'verified', task: 'plant', counts: HONEST }) : null;
  assert.notEqual(r.status, 'verified', `MISSED (${name}): verified with ${r.counts?.steps} steps, ${r.counts?.keystrokes} keys; against an honest path the comparison says ${vs?.verdict}`);
};
const verifySaved = async () => ({ verified: (await (await fetch(base + '/api/state')).json()).saved === 'abcdefghij' });
const verifyApproval = async () => ({ verified: (await (await fetch(base + '/api/approval')).json()).approved === true });

test('control C1: the honest typed save verifies, its write caused by the click', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/form'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'saved'); return {}; },
    verify: verifySaved,
  }, ENTERS);
  assert.equal(r.status, 'verified', r.error);
  assert.deepEqual(r.saved_state.writes_caused.map(w => [w.write, w.by_step]), [['POST /api/save', 3]]);
});

test('control C2: the honest approval verifies', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.click('#approve'); await op.waitFor(() => document.getElementById('out').textContent === 'approved'); return {}; },
    verify: verifyApproval,
  }, SAVES);
  assert.equal(r.status, 'verified', r.error);
});

test('control C3: a save the page sends a few event-loop turns after the click, or once a slow check the click sent is answered, is caused by the click', async () => {
  for (const [button, by] of [['#later', null], ['#checked', 'GET /api/check']]) {
    reset();
    const r = await runDriver(button === '#later' ? {
      async signIn(ctx) { await ctx.page.goto(base + '/shapes'); },
      async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#later'); await op.waitFor(() => document.getElementById('out').textContent === 'saved'); return {}; },
      verify: verifySaved,
    } : {
      async signIn(ctx) { await ctx.page.goto(base + '/shapes'); },
      async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#checked'); await op.waitFor(() => document.getElementById('out').textContent === 'saved'); return {}; },
      verify: verifySaved,
    }, ENTERS);
    assert.equal(r.status, 'verified', `${button}: ${r.error}`);
    const w = r.saved_state.writes_caused.find(x => x.write === 'POST /api/save');
    assert.equal(w.by_step, 3, `${button}: ${JSON.stringify(r.saved_state.writes_caused)}`);
    if (by) assert.equal(w.after_answer_to, by);
  }
});

test('control C4 (routed, p00 r10): a save still under way while the page changes its address stays on the clock and verifies', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/shapes'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#moving'); return {}; },
    verify: verifySaved,
  }, ENTERS);
  assert.equal(r.status, 'verified', r.error);
  // The clock waited for the product's answer (0.8 s): an address change abandons nothing.
  assert.ok(r.counts.machine_seconds >= 0.8, `machine seconds ${r.counts.machine_seconds}: the clock stopped before the save was answered`);
});

test('plant R1 (critic r10): set-up stashes the value as a server-side draft; run() clicks "Save draft" once, typing nothing -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/draft', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/form'); },
    async run(op) { await op.click('#sd'); await op.waitFor(() => document.getElementById('out').textContent === 'saved'); return {}; },
    verify: verifySaved,
  }, ENTERS);
  never('R1', r);
  assert.equal(r.status, 'invalid');
  assert.match(r.error, /gained "abcdefghij", which the measured part never entered \(typed or picked as a file\); the page sent "abcdefghij" in a request it composed itself/);
});

test('plant R2 (critic r10): set-up turns on a setting; the start page sends the declared approval write on its own timer; run() presses Escape -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/settings', { method: 'POST' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.press('Escape'); await op.waitFor(() => document.getElementById('out').textContent === 'approved', { timeout: 9000 }); return {}; },
    verify: verifyApproval,
  }, SAVES);
  never('R2', r);
  assert.equal(r.status, 'invalid');
  assert.match(r.error, /no counted step caused any of the writes the page sent \(POST \/api\/approve: [\d.]+ s after step 1 ended\)/);
});

test('plant R2b: the page approves 2.5 s after the first key it sees (a timer the key armed); R2c: the same while the page polls the back end -> never verifies', async () => {
  for (const mode of ['key', 'poll']) {
    reset();
    const r = await runDriver(mode === 'key' ? {
      async setup() { await fetch(base + '/api/settings', { method: 'POST' }); },
      async signIn(ctx) { await ctx.page.goto(base + '/status-key'); },
      async run(op) { await op.press('Escape'); await op.waitFor(() => document.getElementById('out').textContent === 'approved', { timeout: 9000 }); return {}; },
      verify: verifyApproval,
    } : {
      async setup() { await fetch(base + '/api/settings', { method: 'POST' }); },
      async signIn(ctx) { await ctx.page.goto(base + '/status-poll'); },
      async run(op) { await op.press('Escape'); await op.waitFor(() => document.getElementById('out').textContent === 'approved', { timeout: 9000 }); return {}; },
      verify: verifyApproval,
    }, SAVES);
    never(`R2 ${mode}`, r);
    assert.equal(r.status, 'invalid', `${mode}: ${r.error}`);
    assert.match(r.error, /no counted step caused/, mode);
  }
});

test('plant R2d: a step causes a decoy approval (another record); the page sends the real one on its own timer -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/settings', { method: 'POST' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/status-decoy'); },
    async run(op) { await op.click('#decoy'); await op.waitFor(() => document.getElementById('out').textContent === 'approved', { timeout: 9000 }); return {}; },
    verify: verifyApproval,
  }, SAVES);
  never('R2d', r);
  assert.equal(r.status, 'invalid');
  assert.match(r.error, /the page sent the task's own write on its own beside the one a step caused \(POST \/api\/approve: [\d.]+ s after step 1 ended\)/);
});

test('plant R3 (critic r10): set-up stashes the value; the start page autosaves it on a timer; run() presses Escape -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/draft', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/auto'); },
    async run(op) { await op.press('Escape'); await op.waitFor(() => document.getElementById('out').textContent === 'saved', { timeout: 9000 }); return {}; },
    verify: verifySaved,
  }, ENTERS);
  never('R3', r);
  assert.equal(r.status, 'invalid');
});

// -- set-up's own writes (watchSetUpBrowser) -------------------------------------------------------

/** One set-up browser, watched; `act(page)` does what set-up does; returns the watch's verdict. */
async function setUpWatch(act, { settleMs = 3000 } = {}) {
  const browser = await launch();
  try {
    const watch = watchSetUpBrowser(browser);
    const context = await browser.newContext();
    const page = await context.newPage();
    await act(page, watch);
    await watch.settle(settleMs);
    await context.close();
    return { problem: watch.problem(), waited: watch.waited };
  } finally {
    await browser.close();
  }
}

test('set-up (critic X1): a write the page aborted is named as abandoned, with the browser\'s reason, at once', async () => {
  reset();
  const t = performance.now();
  const { problem } = await setUpWatch(async page => {
    await page.goto(base + '/abort');
    await page.fill('#q', 'abcdefghij');
    await page.click('#go');
    await page.waitForTimeout(400);
  });
  assert.match(problem, /abandoned 1 request\(s\) that change the product before they were answered \(POST \/api\/slow-save \(net::ERR_ABORTED\)\)/);
  assert.ok(performance.now() - t < 2900, 'the watch waited for an aborted write as if it were still under way');
});

test('set-up (routed, p00 r10 and p02 r10): a write under way while the page only changes its address is waited for, not taken as abandoned; a new document still abandons it', async () => {
  reset();
  const moved = await setUpWatch(async page => {
    await page.goto(base + '/shapes');
    await page.evaluate(() => { fetch('/api/slow-save?ms=600', { method: 'POST', body: 'x' }); history.pushState({}, '', '/shapes/next'); });
  });
  assert.equal(moved.problem, null);
  assert.equal(moved.waited, 1);
  reset();
  const replaced = await setUpWatch(async page => {
    await page.goto(base + '/shapes');
    await page.evaluate(() => { fetch('/api/slow-save?ms=600', { method: 'POST', body: 'x' }); });
    await page.goto(base + '/shapes?again');
  });
  assert.match(replaced.problem, /abandoned 1 request\(s\).*POST \/api\/slow-save \(its page moved on\)/);
});

test('set-up (critic r10): a write to an address that names a long-lived channel is watched like any other', async () => {
  reset();
  const { problem } = await setUpWatch(async page => {
    await page.goto(base + '/shapes');
    await page.evaluate(() => { fetch('/api/slow-save?ms=600&via=websocket', { method: 'POST', body: 'x' }); });
    await page.goto(base + '/shapes?again');
  });
  assert.match(problem, /abandoned 1 request\(s\).*POST \/api\/slow-save \(its page moved on\)/);
});

test('set-up (routed): ctx.settleWrites() lets set-up wait for its own write before it moves on; it is refused once the clock runs', async () => {
  reset();
  const r = await runDriver({
    async setup(ctx) {
      await ctx.page.goto(base + '/shapes');
      await ctx.page.locator('#q').fill('zz');
      await ctx.page.locator('#moving').click();
      const s = await ctx.settleWrites();
      ctx.state.settled = s;
      await ctx.page.goto(base + '/form');
    },
    async signIn(ctx) { await ctx.page.goto(base + '/form'); },
    async run(op, ctx) {
      let refused = null;
      try { await ctx.settleWrites(); } catch (e) { refused = String(e.message || e); }
      await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'saved');
      return { refused };
    },
    verify: verifySaved,
  }, ENTERS);
  // The wait itself is refused inside the clock, and a refusal invalidates the run even when caught.
  assert.equal(r.status, 'invalid', r.error);
  assert.match(r.error, /settleWrites\(\) outside set-up and sign-in/);
  // Set-up's own save was answered before set-up moved on: nothing was abandoned.
  assert.doesNotMatch(r.error, /abandoned/);
});
