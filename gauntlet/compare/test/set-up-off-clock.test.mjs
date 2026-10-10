// Round 10 (critic p01 r9, biggest gap): set-up could still do a task that saves off the clock.
//   Q1: a task that names nothing entered accepted any back-end change as the saved state: set-up
//       approved, run() bumped an unrelated version, verify() keyed on the version. On the real
//       api-update-user driver a lost task was recorded as a win on every metric.
//   Q2: set-up's browser sent a slow save and the runner closed it without waiting: the save landed
//       inside the measured part, which pressed one key.
// The defences (lib/runner.mjs): the task declares its end state (the read and the parts of its answer
// that hold it, and the writes that save it); only a change there counts; the measured part must send
// a write (one of the declared ones) and must have entered every value its end state gained; set-up's
// browser writes are answered before the start, and one it abandoned refuses the run. Also pinned here:
// set-up's local storage never reaches a signed-in start (critic X12), and the clipboard is empty at
// the start (critic X16).
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout, compareRuns } from '../lib/runner.mjs';
import { HARNESS_DIR } from '../lib/config.mjs';
import { describeDriverFile, loadTask } from '../lib/registry.mjs';
import { loadNeedles } from '../data/generate.mjs';
import { plantedFile, sandboxed } from './helpers/driver-module.mjs';

const SLOW_MS = 1500;
let server, base, tmp, needles;
let saved = null;
let version = 1;
let approved = false;
let touched = 0;
const timers = new Set();
const later = (ms, fn) => { const t = setTimeout(() => { timers.delete(t); fn(); }, ms); timers.add(t); };
// The identity stand-in for the real api-update-user driver: users, the administrator's preferences.
let users = [];
let prefs = { language: 'en', numerals: 'latn' };

// A screen that shows, live, whether a value is saved (it asks the back end every 100 ms) and a
// Save button whose save is slow; another that saves through a "later" endpoint (a job the product
// runs a moment after it answers).
const LIVE_PAGE = () => `<!doctype html><html><body><input id="q" aria-label="Query"><button id="go">Save</button><button id="mark">Mark</button>
<div id="out">${saved === null ? 'none' : 'a value is saved'}</div><div id="touched"></div>
<script>document.getElementById('go').onclick = () => { fetch('/api/slow-save', { method: 'POST', body: document.getElementById('q').value }); };
document.getElementById('mark').onclick = () => { fetch('/api/mark', { method: 'POST' }).then(() => { document.getElementById('touched').textContent = 'marked'; }); };
setInterval(() => fetch('/api/state').then(r => r.json()).then(s => { document.getElementById('out').textContent = s.saved === null ? 'none' : 'a value is saved'; }).catch(() => {}), 100);</script></body></html>`;
// An approval, an unrelated button that bumps a version, and one that touches the approval record
// without approving it.
const STATUS_PAGE = () => `<!doctype html><html><body><button id="approve">Approve</button><button id="mark">Mark</button><button id="touch">Touch</button>
<div id="out">${approved ? 'approved' : 'waiting'}</div><div id="touched"></div>
<script>document.getElementById('approve').onclick = () => fetch('/api/approve', { method: 'POST' }).then(() => { document.getElementById('out').textContent = 'approved'; });
document.getElementById('mark').onclick = () => { fetch('/api/mark', { method: 'POST' }).then(() => { document.getElementById('touched').textContent = 'marked'; }); };
document.getElementById('touch').onclick = () => { fetch('/api/touch', { method: 'POST' }).then(() => { document.getElementById('touched').textContent = 'touched'; }); };
setInterval(() => fetch('/api/approval').then(r => r.json()).then(s => { if (s.approved) document.getElementById('out').textContent = 'approved'; }).catch(() => {}), 100);</script></body></html>`;
// A form that keeps a draft in the browser's local storage (a product that remembers what was typed).
const REMEMBER_PAGE = () => `<!doctype html><html><body><input id="q" aria-label="Query"><button id="go">Save</button><div id="out"></div>
<script>const q = document.getElementById('q');
if (location.search.includes('keep')) localStorage.setItem('draft', 'abcdefghij');
q.value = localStorage.getItem('draft') || '';
document.getElementById('go').onclick = () => fetch('/api/save', { method: 'POST', body: q.value }).then(() => { document.getElementById('out').textContent = 'saved'; });</script></body></html>`;

before(async () => {
  needles = loadNeedles();
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    const json = (v, status = 200) => { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(v)); };
    const body = fn => { let b = ''; req.on('data', c => { b += c; }); req.on('end', () => fn(b)); };
    if (u.pathname === '/api/slow-save' && req.method === 'POST') {
      // The product keeps working on a save when the browser that sent it has gone, as servers do.
      return body(b => later(SLOW_MS, () => { saved = b; try { res.writeHead(200); res.end(b); } catch { /* client gone */ } }));
    }
    if (u.pathname === '/api/save' && req.method === 'POST') return body(b => { saved = b; res.writeHead(200); res.end(b); });
    // A job the product runs a moment after it answers (set-up schedules it; it lands during the clock).
    if (u.pathname === '/api/save-later' && req.method === 'POST') return body(b => { later(Number(u.searchParams.get('ms') || 2500), () => { saved = b; }); json({ accepted: true }); });
    if (u.pathname === '/api/approve-later' && req.method === 'POST') { later(Number(u.searchParams.get('ms') || 2500), () => { approved = true; }); return json({ accepted: true }); }
    if (u.pathname === '/api/approve' && req.method === 'POST') { approved = true; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/mark' && req.method === 'POST') { version++; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/touch' && req.method === 'POST') { touched++; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/state') return json({ saved });
    if (u.pathname === '/api/approval') return json({ approved, touched });
    if (u.pathname === '/api/version') return json({ version });
    // The identity stand-in.
    if (u.pathname === '/api/auth/sign-in' && req.method === 'POST') return body(() => json({ token: 'api' }));
    if (u.pathname === '/api/auth/session') return json({ authenticated: true, user: { id: 'admin', ...prefs } });
    if (u.pathname === '/api/identity/me/preferences' && req.method === 'PUT') return body(b => { prefs = { ...prefs, ...JSON.parse(b || '{}') }; json(prefs); });
    const one = /^\/api\/identity\/users\/([\w-]+)$/.exec(u.pathname);
    if (one) {
      const user = users.find(x => x.id === one[1]);
      if (!user) return json({}, 404);
      if (req.method === 'PUT') return body(b => { const v = JSON.parse(b || '{}'); user.language = v.language; user.version++; json(user); });
      return json(user);
    }
    if (u.pathname === '/api/identity/users') {
      const q = (u.searchParams.get('search') || '').toLowerCase();
      return json({ items: users.filter(x => !q || x.email.toLowerCase().includes(q) || x.displayName.toLowerCase().includes(q)) });
    }
    res.writeHead(200, { 'Content-Type': 'text/html' });
    res.end(u.pathname.startsWith('/status') ? STATUS_PAGE() : u.pathname.startsWith('/remember') ? REMEMBER_PAGE() : LIVE_PAGE());
  });
  server.keepAliveTimeout = 1;
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-set-up-off-clock-'));
});
after(() => {
  for (const t of timers) clearTimeout(t);
  server?.close(); server?.closeAllConnections?.();
  fs.rmSync(tmp, { recursive: true, force: true });
});

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const APPROVAL = { ours: { reads: [{ read: 'GET /api/approval', parts: ['approved'] }], writes: ['POST /api/approve'] } };
const STATE = { ours: { reads: [{ read: 'GET /api/state', parts: ['saved'] }] } };
// An approval: saves, names nothing entered.
const SAVES = { ...TASK, saves: true, endState: APPROVAL };
// A value the person types and saves.
const ENTERS = { ...TASK, saves: true, input: { value: 'abcdefghij' }, enters: ['value'], endState: STATE };
const standIn = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'admin', password: 'x' } }, brandWords: [], readyKind: 'default' });
const out = () => layout(path.join(tmp, String(Math.random()).slice(2)));
const runDriver = async (driver, task) => execute(task, await sandboxed(driver, { base }), standIn(), 'ours', needles, out(), { timeout: 10_000 });
const reset = () => { for (const t of timers) clearTimeout(t); timers.clear(); saved = null; version = 1; approved = false; touched = 0; };
const HONEST = { steps: 3, keystrokes: 10, machine_seconds: 5, human_seconds: 6, human_plus_wait_seconds: 6.3 };
const never = (r, what) => {
  const vs = compareRuns(r, { status: 'verified', task: 'plant', counts: HONEST });
  assert.notEqual(r.status, 'verified', `MISSED (${what}): verified with ${r.counts?.steps} steps, ${r.counts?.keystrokes} keys, ${r.counts?.machine_seconds} s; against an honest path the comparison says: ${vs.verdict}`);
  assert.notEqual(vs.verdict, 'win');
};
const verifyApproval = async () => ({ verified: (await (await fetch(base + '/api/approval')).json()).approved === true });

test('control C1: the honest approval verifies, and the record names the end state that changed and the write that saved it', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.click('#approve'); await op.waitFor(() => document.getElementById('out').textContent === 'approved'); return {}; },
    verify: verifyApproval,
  }, SAVES);
  assert.equal(r.status, 'verified', r.error);
  assert.deepEqual(r.saved_state.end_state_changed, { 'GET /api/approval': ['approved'] });
  assert.deepEqual(r.saved_state.writes_sent, ['POST /api/approve']);
});

test('control C2: an honest verify() after set-up approved is refused (already done before the clock)', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/approve', { method: 'POST' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.click('#mark'); await op.waitFor(() => document.getElementById('touched').textContent === 'marked'); return {}; },
    verify: verifyApproval,
  }, SAVES);
  never(r, 'C2');
  assert.match(r.error, /already done before the clock/);
});

test('plant Q1 (critic r9): set-up approves through the API, run() bumps an unrelated version, verify() keys on the version -> never verifies', async () => {
  reset();
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
  never(r, 'Q1');
  assert.equal(r.status, 'invalid');
  assert.match(r.error, /end state \(GET \/api\/approval \[approved\]\) did not change/);
  assert.deepEqual(r.saved_state.changed, ['GET /api/version']);
});

test('plant Q1b: the same, run() changing another part of the end-state record (touched, not approved) -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/approve', { method: 'POST' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.click('#touch'); await op.waitFor(() => document.getElementById('touched').textContent === 'touched'); return {}; },
    async verify() { const s = await (await fetch(base + '/api/approval')).json(); return { verified: s.touched > 0 && s.approved === true }; },
  }, SAVES);
  never(r, 'Q1b');
  assert.match(r.error, /did not change/);
  assert.deepEqual(r.saved_state.changed_parts['GET /api/approval'], ['touched']);
});

test('plant Q1 on the real driver (critic r9): ours api-update-user whose set-up switches the language and whose run() sends one unrelated preference -> never verifies; the driver as written verifies', async () => {
  const task = await loadTask('api-update-user');
  const file = path.join(HARNESS_DIR, 'drivers', 'ours', 'api-update-user.mjs');
  const fresh = () => { users = [{ id: 'u1', email: needles.user.login, displayName: needles.user.name, language: 'en', isActive: true, roleIds: [], version: 1 }]; prefs = { language: 'en', numerals: 'latn' }; };
  fresh();
  const honest = await execute(task, await describeDriverFile(file), standIn(), 'ours', needles, out(), { timeout: 10_000 });
  assert.equal(honest.status, 'verified', `the driver as written, on the stand-in: ${honest.error}`);
  assert.deepEqual(honest.saved_state.end_state_changed, { 'GET /api/identity/users/u1': ['language'] });
  // The critic's plant (gauntlet/evidence/p01-odoo-rig/r9/plants/plant-Q1-api-update-user.diff), on the driver as it stands.
  const planted = plantedFile(file, s0 => {
    const s1 = s0.replace("    if (user.language !== 'en') await setLanguage(api, user, 'en');\n", `    if (user.language !== 'en') await setLanguage(api, user, 'en');
    const fresh = await api.get(\`/api/identity/users/\${user.id}\`);
    await setLanguage(api, fresh, 'ar');
    await api.put('/api/identity/me/preferences', { language: 'en', numerals: 'arab' });
`);
    const s2 = s1.replace('  async run(op, ctx) {\n', `  async run(op, ctx) {
    await op.request('PUT', '/api/identity/me/preferences', { language: 'en', numerals: 'latn' }, { label: 'one unrelated request' });
    return {};
  },
  async runHonest(op, ctx) {
`);
    const s3 = s2.replace("    return { verified: u.language === 'ar', details:", "    const me = (await api.get('/api/auth/session')).user;\n    return { verified: me.numerals === 'latn' && u.language === 'ar', details:");
    assert.ok(s1 !== s0 && s2 !== s1 && s3 !== s2, 'the driver changed: update this plant');
    return s3;
  });
  fresh();
  const r = await execute(task, await describeDriverFile(planted), standIn(), 'ours', needles, out(), { timeout: 10_000 });
  never(r, 'Q1 on api-update-user');
  assert.equal(r.status, 'invalid');
  assert.match(r.error, /end state \(GET \/api\/identity\/users\/\* \[language\]\) did not change/);
  assert.deepEqual(r.saved_state.changed, ['GET /api/auth/session']);
});

test('plant Q2 (critic r9): set-up types the value and clicks a slow Save in its own browser; the save would land after the clock starts; run() presses one key -> never verifies (the save is answered before the start)', async () => {
  reset();
  const r = await runDriver({
    async setup(ctx) { await ctx.page.goto(base + '/live'); await ctx.page.fill('#q', 'abcdefghij'); await ctx.page.click('#go'); },
    async signIn(ctx) { await ctx.page.goto(base + '/live'); },
    async run(op) { await op.press('Escape'); await op.waitFor(() => document.getElementById('out').textContent === 'a value is saved', { timeout: 9000 }); return {}; },
    async verify() { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' }; },
  }, ENTERS);
  never(r, 'Q2');
  // Sign-in moved set-up's page on while the save was under way: the browser abandoned it.
  assert.equal(r.status, 'invalid');
  assert.match(r.error, /abandoned 1 request\(s\) that change the product.*POST \/api\/slow-save \(its page moved on\)/);
});

test('plant Q2b: set-up clicks the slow Save and moves its page on at once (the browser abandons the save, the product still carries it out) -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup(ctx) { await ctx.page.goto(base + '/live'); await ctx.page.fill('#q', 'abcdefghij'); await ctx.page.click('#go'); await ctx.page.goto(base + '/live?again'); },
    async signIn(ctx) { await ctx.page.goto(base + '/live'); },
    async run(op) { await op.press('Escape'); await op.waitFor(() => document.getElementById('out').textContent === 'a value is saved', { timeout: 9000 }); return {}; },
    async verify() { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' }; },
  }, ENTERS);
  never(r, 'Q2b');
  assert.equal(r.status, 'invalid');
  assert.match(r.error, /abandoned 1 request\(s\) that change the product.*POST \/api\/slow-save/);
});

test('plant Q2d: set-up clicks the slow Save in a page of its own and closes that page -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup(ctx) { const p = await ctx.context.newPage(); await p.goto(base + '/live'); await p.fill('#q', 'abcdefghij'); await p.click('#go'); await p.close(); },
    async signIn(ctx) { await ctx.page.goto(base + '/live'); },
    async run(op) { await op.press('Escape'); await op.waitFor(() => document.getElementById('out').textContent === 'a value is saved', { timeout: 9000 }); return {}; },
    async verify() { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' }; },
  }, ENTERS);
  never(r, 'Q2d');
  assert.equal(r.status, 'invalid');
  assert.match(r.error, /abandoned 1 request\(s\) that change the product.*POST \/api\/slow-save/);
});

test('control: set-up\'s own save in its browser, answered before the start, is waited for and then seen as done before the clock', async () => {
  reset();
  const r = await runDriver({
    async setup(ctx) { await ctx.page.goto(base + '/live'); await ctx.page.fill('#q', 'abcdefghij'); await ctx.page.click('#go'); },
    async run(op) { await op.press('Escape'); return {}; },
    async verify() { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' }; },
  }, ENTERS);
  never(r, 'set-up save waited for');
  assert.equal(r.set_up_writes_waited, 1, 'the runner waited for the save set-up\'s browser sent');
  assert.match(r.error, /already done before the clock|already shows "abcdefghij"/);
});

test('plant Q2c: set-up sends the slow Save from a browser context of its own and leaves it -> never verifies (that context is watched too)', async () => {
  reset();
  const r = await runDriver({
    async setup(ctx) {
      const other = await ctx.browser.newContext();
      const p = await other.newPage();
      await p.goto(base + '/live'); await p.fill('#q', 'abcdefghij'); await p.click('#go');
    },
    async signIn(ctx) { await ctx.page.goto(base + '/live'); },
    async run(op) { await op.press('Escape'); await op.waitFor(() => document.getElementById('out').textContent === 'a value is saved', { timeout: 9000 }); return {}; },
    async verify() { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' }; },
  }, ENTERS);
  never(r, 'Q2c');
  assert.match(r.error, /already done before the clock/);
  assert.equal(r.set_up_writes_waited, 1);
});

test('plant Q3: set-up has the product save the value a moment later (a job); the measured part sends nothing -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save-later?ms=2500', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/live'); },
    async run(op) { await op.press('Escape'); await op.waitFor(() => document.getElementById('out').textContent === 'a value is saved', { timeout: 9000 }); return {}; },
    async verify() { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' }; },
  }, ENTERS);
  never(r, 'Q3');
  assert.match(r.error, /sent nothing that writes to the product|already done before the clock/);
});

test('plant Q3b: the same, the measured part sending an unrelated write -> never verifies (the value it gained was never entered)', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save-later?ms=2500', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/live'); },
    async run(op) { await op.click('#mark'); await op.waitFor(() => document.getElementById('out').textContent === 'a value is saved', { timeout: 9000 }); return {}; },
    async verify() { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' }; },
  }, ENTERS);
  never(r, 'Q3b');
  assert.match(r.error, /gained "abcdefghij", which the measured part never entered|already done before the clock/);
});

test('plant Q3c: set-up has the product approve a moment later; the measured part sends an unrelated write -> never verifies (none of the writes that save the approval)', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/approve-later?ms=2500', { method: 'POST' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.click('#mark'); await op.waitFor(() => document.getElementById('out').textContent === 'approved', { timeout: 9000 }); return {}; },
    verify: verifyApproval,
  }, SAVES);
  never(r, 'Q3c');
  assert.match(r.error, /none of the writes that save the task's end state \(POST \/api\/approve\)|already done before the clock/);
});

test('control: the honest typed save verifies (the value typed, the write sent, the end state gained it)', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/remember'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'saved'); return {}; },
    async verify() { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' }; },
  }, ENTERS);
  assert.equal(r.status, 'verified', r.error);
  assert.deepEqual(r.saved_state.end_state_gained, ['abcdefghij']);
});

test('X12 (critic r9): what set-up left in the browser\'s local storage never reaches a signed-in start (a remembered draft would fill the form)', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/remember?keep'); await ctx.page.goto(base + '/remember'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'saved'); return {}; },
    async verify() { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' }; },
  }, ENTERS);
  assert.equal(r.status, 'verified', `${r.status} ${r.error}`);
  assert.deepEqual(r.start_state.filled, [], 'the start form holds nothing set-up left');
});

test('X16 (critic r9): the clipboard is emptied at the start and read back: what set-up copied is gone', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) {
      await ctx.page.goto(base + '/remember');
      await ctx.page.locator('#q').fill('SECRETCLIP');
      await ctx.page.locator('#q').press('Control+a');
      await ctx.page.locator('#q').press('Control+c');
      await ctx.page.locator('#q').fill('');
    },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'saved'); return {}; },
    async verify() { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' }; },
  }, ENTERS);
  assert.equal(r.status, 'verified', `${r.status} ${r.error}`);
});
