// Round 9 (critic p01 r8, biggest gap): set-up may act on the product (fixtures), so the only thing
// that keeps set-up from doing the task itself, off the clock, is the check that the task is not
// already done when the clock starts. The critic defeated it: verify() was called with no outcome
// before the clock and with run()'s outcome after it, so it could answer "not done" before the clock
// only, and a run with no counted step was accepted. These plants (P1, P1b, P2 are the critic's own)
// and the channels a driver could use instead (its process's memory, a file, a clock, the screen, a
// back-end read that changes for another reason) must all end in a run that never verifies.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout, compareRuns, containsValue, endStateProblems, measuredPartProblem, savedState, taskRuleProblems, VERIFY_PASS_GAP_MS } from '../lib/runner.mjs';
import { sandboxed, sandboxedSource } from './helpers/driver-module.mjs';
import { readRecord } from '../lib/sandbox/bridge.mjs';
import { DRIVERS_DIR } from '../lib/registry.mjs';
import { lintDriver } from './drivers-lint.test.mjs';

let server, base, tmp;
let saved = null;
let version = 1;
let posts = 0;
// The critic's stand-in: the saved value shows on the page.
const PAGE = () => `<!doctype html><html><body><input id="q" aria-label="Query"><button id="go">Save</button><div id="out">${saved === null ? 'none' : `saved ${saved}`}</div>
<script>document.getElementById('go').onclick = () => { const v = document.getElementById('q').value;
  fetch('/api/save', { method: 'POST', body: v }).then(r => r.text()).then(t => { document.getElementById('out').textContent = 'saved ' + t; }); };</script></body></html>`;
// A form that does not show the saved value (a list start, a home screen): only whether one is saved.
// "Touch" changes the screen and nothing else; "Mark" saves nothing the task asks for but changes a
// version number in the back end.
const STATUS_PAGE = () => `<!doctype html><html><body><input id="q" aria-label="Query"><button id="go">Save</button><button id="touch">Touch</button><button id="mark">Mark</button>
<div id="out">${saved === null ? 'none' : 'a value is saved'}</div><div id="touched"></div>
<script>document.getElementById('go').onclick = () => { const v = document.getElementById('q').value;
  fetch('/api/save', { method: 'POST', body: v }).then(r => r.text()).then(() => { document.getElementById('out').textContent = 'a value is saved'; }); };
document.getElementById('touch').onclick = () => { document.getElementById('touched').textContent = 'touched'; };
document.getElementById('mark').onclick = () => { fetch('/api/mark', { method: 'POST' }).then(() => { document.getElementById('touched').textContent = 'marked'; }); };</script></body></html>`;

// A record form: its field shows the saved value (a draft saved in set-up would be there).
const FORM_PAGE = () => `<!doctype html><html><body><input id="q" aria-label="Query" value="${saved ?? ''}"><button id="go">Save</button><div id="out"></div>
<script>document.getElementById('go').onclick = () => fetch('/api/save', { method: 'POST', body: document.getElementById('q').value }).then(() => { document.getElementById('out').textContent = 'saved'; });</script></body></html>`;

before(async () => {
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    if (u.pathname === '/api/save' && req.method === 'POST') { let b = ''; req.on('data', c => { b += c; }); req.on('end', () => { posts++; saved = b; res.writeHead(200); res.end(b); }); return; }
    if (u.pathname === '/api/mark' && req.method === 'POST') { version++; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/state') { res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ saved })); }
    if (u.pathname === '/api/version') { res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ version })); }
    if (u.pathname === '/api/now') { res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ now: Date.now() })); }
    res.writeHead(200, { 'Content-Type': 'text/html' });
    res.end(u.pathname === '/status' ? STATUS_PAGE() : u.pathname === '/form' ? FORM_PAGE() : PAGE());
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-before-clock-'));
});
after(() => { server?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
// The same task, declared as the product's back end holding its end state, and naming what the person enters.
// Round 10: a task that saves declares its end state (the back-end read and the part that holds it).
const STATE = { ours: { reads: [{ read: 'GET /api/state', parts: ['saved'] }] } };
const SAVES = { ...TASK, saves: true, endState: STATE };
const ENTERS = { ...TASK, saves: true, input: { value: 'abcdefghij' }, enters: ['value'], endState: STATE };
/** The same task with its end state declared at another read (the record-only checks below). */
const at = (task, read, parts) => ({ ...task, endState: { ours: { reads: [{ read, parts }] } } });
const standIn = () => ({ id: 'ours', baseUrl: base, users: {}, brandWords: [], readyKind: 'default' });
const out = () => layout(path.join(tmp, String(Math.random()).slice(2)));
const runDriver = async (driver, task = TASK) => execute(task, await sandboxed(driver, { base }), standIn(), 'ours', {}, out(), { timeout: 10_000 });
const runSource = async (source, task = TASK) => execute(task, await sandboxedSource(source.replaceAll('BASE', base)), standIn(), 'ours', {}, out(), { timeout: 10_000 });
const reset = () => { saved = null; version = 1; };
const HONEST = { steps: 3, keystrokes: 10, machine_seconds: 0.5, human_seconds: 6, human_plus_wait_seconds: 6.3 };
const never = (r, what) => {
  const vs = compareRuns(r, { status: 'verified', task: 'plant', counts: HONEST });
  assert.notEqual(r.status, 'verified', `MISSED (${what}): verified with ${r.counts?.steps} steps, ${r.counts?.keystrokes} keys, ${r.counts?.machine_seconds} s; against an honest 3-step path the comparison says: ${vs.verdict}`);
  assert.notEqual(vs.verdict, 'win');
};

test('control: the honest path verifies (3 steps, 10 keys)', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'saved abcdefghij', { label: 'saved' }); return { done: true }; },
    async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' }; },
  });
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.counts.steps, 3);
  assert.equal(r.counts.keystrokes, 10);
  assert.equal(r.verify_before.verified, false);
});

test('control: a driver whose set-up did the task and whose verify() is honest is refused (already done before the clock)', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.press('Escape'); return { done: true }; },
    async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' }; },
  });
  never(r, 'honest verify after set-up did it');
  assert.equal(r.status, 'invalid');
  assert.match(r.error, /already done before the clock/);
});

test('plant P1 (critic r8): set-up does the task through the API; verify() answers "not done" when it gets no outcome; run() does nothing -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run() { return { done: true }; },
    async verify(ctx, outcome) {
      if (outcome === undefined) return { verified: false };
      return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' };
    },
  });
  never(r, 'P1');
  assert.equal(r.status, 'invalid');
  assert.match(r.error, /no counted step/);
});

test('plant P1b (critic r8): the same with verify() throwing when it gets no outcome -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.press('Escape'); return { done: true }; },
    async verify(ctx, outcome) {
      if (!outcome?.done) throw new Error('needs the outcome of run()');
      return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' };
    },
  });
  never(r, 'P1b');
});

test('plant P2 (critic r8): set-up saves the value through the API; the measured part only waits -> never verifies', async () => {
  reset();
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
  never(r, 'P2');
  assert.match(r.error, /no counted step/);
});

test('plant P3: run() leaves a mark in its process (a module variable, a global, ctx.state); verify() answers "not done" without it -> never verifies', async () => {
  reset();
  const r = await runSource(`
let ran = false;
export default {
  async setup() { await fetch('BASE/api/save', { method: 'POST', body: 'abcdefghij' }); },
  async signIn(ctx) { await ctx.page.goto('BASE/plant'); },
  async run(op, ctx) { ran = true; globalThis.__ran = true; ctx.state.ran = true; await op.press('Escape'); return {}; },
  async verify(ctx) {
    if (!ran && !globalThis.__ran && !ctx.state.ran) return { verified: false };
    return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' };
  },
};`);
  never(r, 'P3');
  assert.equal(r.status, 'failed', 'verify() never saw the mark, so it never passed');
});

test('plant P4: run() writes a file in its scratch folder; verify() looks for it there and in every folder beside its own -> never verifies', async () => {
  reset();
  const r = await runSource(`
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
export default {
  async setup(ctx) { await fetch('BASE/api/save', { method: 'POST', body: 'abcdefghij' }); ctx.state.scratch = os.tmpdir(); },
  async signIn(ctx) { await ctx.page.goto('BASE/plant'); },
  async run(op) { fs.writeFileSync(path.join(os.tmpdir(), 'ran.txt'), 'ran'); await op.press('Escape'); return {}; },
  async verify(ctx) {
    const found = () => {
      try { if (fs.existsSync(path.join(ctx.state.scratch, 'ran.txt')) || fs.readFileSync(path.join(ctx.state.scratch, 'ran.txt'), 'utf8')) return true; } catch { /* refused */ }
      try { return fs.readdirSync(path.dirname(os.tmpdir())).some(d => { try { return fs.existsSync(path.join(path.dirname(os.tmpdir()), d, 'ran.txt')); } catch { return false; } }); } catch { return false; }
    };
    if (!found()) return { verified: false };
    return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' };
  },
};`);
  never(r, 'P4');
});

test('plant P5: set-up notes the time; verify() answers "not done" until enough time has passed, by every clock a process can read -> never verifies', async () => {
  reset();
  const r = await runSource(`
import os from 'node:os';
export default {
  async setup(ctx) {
    await fetch('BASE/api/save', { method: 'POST', body: 'abcdefghij' });
    ctx.state.at = { date: Date.now(), perf: performance.now() + performance.timeOrigin, uptime: os.uptime(), hr: Number(process.hrtime.bigint() / 1000000n), puptime: process.uptime(),
      cpu: os.cpus().reduce((s, c) => s + c.times.user + c.times.sys + c.times.idle, 0) };
  },
  async signIn(ctx) { await ctx.page.goto('BASE/plant'); },
  async run(op) { await op.press('Escape'); await op.waitFor(() => false, { timeout: 1200 }).catch(() => {}); await op.press('Escape'); return {}; },
  async verify(ctx) {
    const a = ctx.state.at;
    const later = Date.now() - a.date > 1500 || performance.now() + performance.timeOrigin - a.perf > 1500 || os.uptime() - a.uptime > 1
      || Number(process.hrtime.bigint() / 1000000n) - a.hr > 1500 || os.cpus().reduce((s, c) => s + c.times.user + c.times.sys + c.times.idle, 0) - a.cpu > 1000;
    if (!later) return { verified: false };
    return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' };
  },
};`);
  never(r, 'P5');
});

test('plant P6: a task saved in the product; set-up saves it, run() touches the screen, verify() reads the back end only once the screen is touched -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.click('#touch'); await op.waitFor(() => document.getElementById('touched').textContent === 'touched'); return {}; },
    async verify(ctx) {
      if ((await ctx.page.locator('#touched').textContent()) !== 'touched') return { verified: false };
      const s = await (await fetch(base + '/api/state')).json();
      return { verified: s.saved === 'abcdefghij' };
    },
  }, SAVES);
  never(r, 'P6');
  assert.equal(r.status, 'invalid');
  assert.match(r.error, /answered differently after the clock|read nothing/);
});

test('plant P6b: the same, verify() reading the back end first both times -> never verifies (the read answered the same before the clock)', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.click('#touch'); await op.waitFor(() => document.getElementById('touched').textContent === 'touched'); return {}; },
    async verify(ctx) {
      const s = await (await fetch(base + '/api/state')).json();
      return { verified: s.saved === 'abcdefghij' && (await ctx.page.locator('#touched').textContent()) === 'touched' };
    },
  }, SAVES);
  never(r, 'P6b');
  assert.match(r.error, /answered differently after the clock/);
  assert.deepEqual(r.saved_state.changed, []);
});

test('plant P7: a task that names what the person enters; set-up saves the value, run() changes something else in the back end (a version), verify() keys on it -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup(ctx) { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); ctx.state.v0 = (await (await fetch(base + '/api/version')).json()).version; },
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.click('#mark'); await op.waitFor(() => document.getElementById('touched').textContent === 'marked'); return {}; },
    async verify(ctx) {
      const { version: v } = await (await fetch(base + '/api/version')).json();
      const s = await (await fetch(base + '/api/state')).json();
      return { verified: v > ctx.state.v0 && s.saved === 'abcdefghij' };
    },
  }, ENTERS);
  never(r, 'P7');
  assert.match(r.error, /gained a value the task asks the person to enter/);
  assert.deepEqual(r.saved_state.changed, ['GET /api/version']);
});

test('plant P8: set-up saves the value the person is to enter, so the start form\'s field already holds it; the measured part clicks Save -> refused at the start', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/form'); },
    async run(op) { await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'saved'); return {}; },
    async verify(ctx) { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' }; },
  }, { ...ENTERS, startAt: 'record' }); // a record start: its fields may hold the record's values
  never(r, 'P8');
  assert.match(r.error, /start screen already shows "abcdefghij" in a field/);
});

test('plant P8b: the same value shown as text beside the form (a record\'s history, say) is no unfair start, but the saved-state check still refuses the run', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.click('#go'); return {}; },
    async verify(ctx) { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' || s.saved === '' }; },
  }, ENTERS);
  never(r, 'P8b');
  assert.doesNotMatch(String(r.error), /unfair start state/);
});

test('plant P9: verify() reads a back-end clock beside the saved state; the clock changes, but not as a saved state does (it differs between the passes) -> never verifies', async () => {
  reset();
  const r = await runDriver({
    async setup(ctx) { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); ctx.state.t0 = (await (await fetch(base + '/api/now')).json()).now; },
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.click('#touch'); await op.waitFor(() => false, { timeout: 1500 }).catch(() => {}); await op.click('#touch'); return {}; },
    async verify(ctx) {
      const { now } = await (await fetch(base + '/api/now')).json();
      const s = await (await fetch(base + '/api/state')).json();
      return { verified: now - ctx.state.t0 > 1500 && s.saved === 'abcdefghij' };
    },
  }, SAVES);
  never(r, 'P9');
  // Refused after the clock (the clock read is no saved change), or already before it on a slow machine.
  assert.match(r.error, /answered differently after the clock|already done before the clock/);
});

test('plant P10: no verify() at all; set-up does the task and run() says it is done -> never verifies (run() never reports its own end state)', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.press('Escape'); return { verified: true }; },
  });
  never(r, 'P10');
  assert.match(r.error, /no verify\(\)/);
});

test('saved state: the honest path of a task that saves what the person enters verifies, and the record names the read that gained it', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/status'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'a value is saved'); return {}; },
    async verify(ctx) { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' }; },
  }, ENTERS);
  assert.equal(r.status, 'verified', r.error);
  assert.deepEqual(r.saved_state.changed, ['GET /api/state']);
  assert.deepEqual(r.saved_state.gained_entered_value, ['GET /api/state']);
  // The second pass after the clock starts at least the gap after the first (a time read differs).
  // A read of the time to the second differs between the passes only when they are over a second apart.
  assert.ok(VERIFY_PASS_GAP_MS >= 1100, `the gap is ${VERIFY_PASS_GAP_MS} ms`);
  assert.ok(r.verify_passes[1].gap_seconds >= 1.1, `the passes were ${r.verify_passes[1].gap_seconds} s apart`);
});

test('saved state: the rule judged on records alone (changed and stable, gained an entered value, nothing read)', () => {
  const rec = (key, text) => readRecord('GET', `http://x${key}`, '', 200, Buffer.from(text));
  const t = at(ENTERS, 'GET /s', ['saved']);
  const ss = (task, b, a, c) => savedState(task, b, a, c, 'ours');
  assert.equal(ss(t, [rec('/s', '{"saved":null}')], [rec('/s', '{"saved":"abcdefghij"}')], [rec('/s', '{"saved":"abcdefghij"}')]).problem, null);
  assert.match(ss(t, [rec('/s', '{"saved":"abcdefghij"}')], [rec('/s', '{"saved":"abcdefghij"}')], [rec('/s', '{"saved":"abcdefghij"}')]).problem, /answered differently/);
  assert.match(ss(t, [], [], []).problem, /read nothing/);
  // A read that differs between the two passes after the clock is a clock, not a saved state.
  assert.match(ss(at(SAVES, 'GET /n', ['n']), [rec('/n', '1')], [rec('/n', '2')], [rec('/n', '3')]).problem, /answered differently/);
  // Read only after the clock: never compared, so never a change.
  assert.match(ss(at(SAVES, 'GET /s', ['saved']), [], [rec('/s', 'a')], [rec('/s', 'a')]).problem, /answered differently/);
  // Changed, but what the person enters was there before.
  assert.match(ss(t, [rec('/v', '1'), rec('/s', 'abcdefghij')], [rec('/v', '2'), rec('/s', 'abcdefghij')], [rec('/v', '2'), rec('/s', 'abcdefghij')]).problem, /gained a value/);
  // A JSON answer is compared part by part: a token that changes with every answer is no saved
  // state; the user the session names is (round 9: Odoo's session information carries such a token).
  const session = (uid, token) => JSON.stringify({ result: { uid, token, lang: 'en_US' } });
  const st = at(SAVES, 'GET /session', ['result.uid']);
  const ok = ss(st, [rec('/session', '{"error":{"message":"Session Expired","timestamp":1}}')], [rec('/session', session(7, 'a1'))], [rec('/session', session(7, 'b2'))]);
  assert.equal(ok.problem, null);
  assert.deepEqual(ok.record.changed_parts['GET /session'].sort(), ['error.message', 'error.timestamp', 'result.lang', 'result.uid'].sort());
  assert.deepEqual(ok.record.end_state_changed, { 'GET /session': ['result.uid'] });
  // Only the token changed (and differently in each pass): nothing saved.
  assert.match(ss(st, [rec('/session', session(7, 'x0'))], [rec('/session', session(7, 'a1'))], [rec('/session', session(7, 'b2'))]).problem, /answered differently/);
  // The entered value must arrive in a part that changed.
  const contact = phone => JSON.stringify({ result: [{ id: 5, phone, write_date: phone ? '2026-10-09 10:00:01' : '2026-10-09 10:00:00' }] });
  const ct = at(ENTERS, 'GET /c', ['result.phone']);
  const contactOk = ss(ct, [rec('/c', contact('+971 4 000'))], [rec('/c', contact('abcdefghij'))], [rec('/c', contact('abcdefghij'))]);
  assert.equal(contactOk.problem, null);
  assert.deepEqual(contactOk.gained_values, ['abcdefghij']);
});

// Round 10 (critic p01 r9, plant Q1): only a change in the task's declared end state counts.
test('saved state (round 10): only a change in the declared end state (its read, its parts) counts, and an entered value must arrive there', () => {
  const rec = (key, text) => readRecord('GET', `http://x${key}`, '', 200, Buffer.from(text));
  const ss = (task, b, a, c, product = 'ours') => savedState(task, b, a, c, product);
  const approval = (approved, touched = 0) => JSON.stringify({ approved, touched });
  const task = at(SAVES, 'GET /api/approval', ['approved']);
  // The honest change.
  assert.equal(ss(task, [rec('/api/approval', approval(false))], [rec('/api/approval', approval(true))], [rec('/api/approval', approval(true))]).problem, null);
  // Q1: the end state was there before the clock; only a version elsewhere moved.
  const q1 = ss(task, [rec('/api/version', '{"version":1}'), rec('/api/approval', approval(true))], [rec('/api/version', '{"version":2}'), rec('/api/approval', approval(true))], [rec('/api/version', '{"version":2}'), rec('/api/approval', approval(true))]);
  assert.match(q1.problem, /end state \(GET \/api\/approval \[approved\]\) did not change/);
  assert.deepEqual(q1.record.changed, ['GET /api/version']);
  // Q1b: the end-state read changed, but in a part that does not hold the end state.
  assert.match(ss(task, [rec('/api/approval', approval(true, 0))], [rec('/api/approval', approval(true, 1))], [rec('/api/approval', approval(true, 1))]).problem, /did not change/);
  // The end-state read with a path segment for the record: `*` is one segment, never more or none.
  const users = at(SAVES, 'GET /api/identity/users/*', ['language']);
  const user = l => JSON.stringify({ id: 'u1', language: l, version: l === 'ar' ? 2 : 1 });
  assert.equal(ss(users, [rec('/api/identity/users/u1', user('en'))], [rec('/api/identity/users/u1', user('ar'))], [rec('/api/identity/users/u1', user('ar'))]).problem, null);
  assert.match(ss(users, [rec('/api/identity/users/u1/x', user('en'))], [rec('/api/identity/users/u1/x', user('ar'))], [rec('/api/identity/users/u1/x', user('ar'))]).problem, /did not change/);
  // A task that declares no end state for the product is refused.
  assert.match(ss(task, [rec('/api/approval', approval(false))], [rec('/api/approval', approval(true))], [rec('/api/approval', approval(true))], 'odoo').problem, /declares no end state for odoo/);
  assert.match(ss({ ...SAVES, endState: undefined }, [], [], []).problem, /declares no end state for ours/);
  // An entered value that changed only outside the end state's parts.
  const entered = at(ENTERS, 'GET /api/record', ['phone']);
  const record = (phone, note) => JSON.stringify({ phone, note });
  assert.match(ss(entered, [rec('/api/record', record('+971', 'x'))], [rec('/api/record', record('+972', 'abcdefghij'))], [rec('/api/record', record('+972', 'abcdefghij'))]).problem, /end state \(GET \/api\/record \[phone\]\) gained no value/);
  // Declarations are checked: reads with parts, writes as "METHOD /path" that are not reads.
  assert.deepEqual(endStateProblems({ ours: { reads: [{ read: 'GET /a/*', parts: ['x.y'] }], writes: ['PUT /a/*'] } }), []);
  assert.equal(endStateProblems({ ours: { reads: [] } }).length, 1);
  assert.equal(endStateProblems({ ours: { reads: [{ read: 'GET a', parts: ['x'] }] } }).length, 1);
  assert.equal(endStateProblems({ ours: { reads: [{ read: 'GET /a', parts: [] }] } }).length, 1);
  assert.equal(endStateProblems({ ours: { reads: [{ read: 'GET /a', parts: ['x'] }], writes: ['GET /a'] } }).length, 1);
  assert.equal(endStateProblems({ ours: { reads: [{ read: 'GET /a', parts: ['x'] }], other: 1 } }).length, 1);
});

// Round 10 (critic p01 r9, plants Q1 and Q2): the measured part itself sent the change.
test('measured part (round 10): a task that saves needs a write sent by the measured part, one of its declared writes, and every entered value its end state gained entered by it', () => {
  const t = { ...ENTERS, endState: { ours: { reads: [{ read: 'GET /api/state', parts: ['saved'] }], writes: ['POST /api/save'] } } };
  const type = text => ({ kind: 'type', text });
  const key = chord => ({ kind: 'key', chord });
  const write = (url, body = null) => ({ method: 'POST', url: `http://x${url}`, body });
  assert.equal(measuredPartProblem(TASK, {}), null, 'a task that does not save');
  // Nothing written (Q2: a key and a wait).
  assert.match(measuredPartProblem(t, { steps: [key('Escape')], sent: [], gained: ['abcdefghij'], productId: 'ours' }), /sent nothing that writes/);
  // A write, but not the one that saves the end state (Q1: a version bumped).
  assert.match(measuredPartProblem(t, { steps: [{ kind: 'click' }], sent: [write('/api/mark')], gained: [], productId: 'ours' }), /none of the writes that save the task's end state \(POST \/api\/save\); it sent POST \/api\/mark/);
  // The declared write, but the value it saved was never entered (set-up's slow save landed instead).
  assert.match(measuredPartProblem(t, { steps: [{ kind: 'click' }], sent: [write('/api/save', 'zzz')], gained: ['abcdefghij'], productId: 'ours' }), /gained "abcdefghij", which the measured part never entered/);
  // Typed (in two pieces), picked, sent in a write's body or address (form-encoded too), or typed into an API request.
  assert.equal(measuredPartProblem(t, { steps: [type('abcde'), type('fghij')], sent: [write('/api/save')], gained: ['abcdefghij'], productId: 'ours' }), null);
  assert.equal(measuredPartProblem(t, { steps: [], sent: [write('/api/save', 'v=abcde%2Ffghij')], gained: ['abcde/fghij'], productId: 'ours' }), null);
  assert.equal(measuredPartProblem({ ...t, endState: { ours: { reads: t.endState.ours.reads, writes: ['PUT /api/users/*'] } } },
    { steps: [{ kind: 'request', writes: true, text: 'PUT /api/users/u1 {"language":"abcdefghij"}' }], sent: [], gained: ['abcdefghij'], productId: 'ours' }), null);
  assert.equal(measuredPartProblem(t, { steps: [{ kind: 'file-pick', file: 'abcdefghij.pdf' }], sent: [write('/api/save')], gained: ['abcdefghij.pdf'], productId: 'ours' }), null);
  // An API read is no write.
  assert.match(measuredPartProblem(t, { steps: [{ kind: 'request', writes: false, text: 'GET /api/state' }], sent: [], gained: [], productId: 'ours' }), /sent nothing that writes/);
  // No declared writes: any write will do (the end-state read still has to change).
  assert.equal(measuredPartProblem(SAVES, { steps: [{ kind: 'click' }], sent: [write('/api/anything')], gained: [], productId: 'ours' }), null);
});

test('entered values are found as a person would have entered them, never inside a longer word or number', () => {
  assert.equal(containsValue('{"phone":"+971505550199"}', '+971 50 555 0199'), true);
  assert.equal(containsValue('phone +971-50-555-0199', '+971 50 555 0199'), true);
  assert.equal(containsValue('9715055501990', '+971 50 555 0199'), false);
  assert.equal(containsValue('{"rate":4.28750000}', '4.2875'), true);
  assert.equal(containsValue('{"rate":4.2876}', '4.2875'), false);
  assert.equal(containsValue('LR-77310', 'LR-7731'), false);
  assert.equal(containsValue('licence ref', 'Licence ref'), true);
});

test('a keyboard-only task fails on a pointer step; the harness judges it, not verify()', async () => {
  const KB = { ...TASK, keyboardOnly: true };
  assert.deepEqual(taskRuleProblems(KB, [{ n: 1, kind: 'key', label: 'Tab' }, { n: 2, kind: 'type', label: 'x' }]), []);
  assert.match(taskRuleProblems(KB, [{ n: 1, kind: 'key', label: 'Tab' }, { n: 2, kind: 'click', label: 'Save' }])[0], /keyboard-only and 1 step used the mouse \(first: step 2, click "Save"\)/);
  assert.deepEqual(taskRuleProblems(TASK, [{ n: 1, kind: 'click', label: 'Save' }]), []);
  reset();
  const verify = async ctx => ({ verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' });
  const pointer = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'saved abcdefghij'); return {}; },
    verify,
  }, KB);
  assert.equal(pointer.status, 'failed');
  assert.match(pointer.task_rules[0], /used the mouse/);
  reset();
  const keys = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.press('Tab'); await op.type('abcdefghij'); await op.press('Tab'); await op.press('Enter'); await op.waitFor(() => document.getElementById('out').textContent === 'saved abcdefghij'); return {}; },
    verify,
  }, KB);
  assert.equal(keys.status, 'verified', keys.error);
});

test('plant P1 on the real driver (critic r8): ours edit-and-save whose set-up saves the number and whose verify() keys on the outcome is caught in review by the lint', () => {
  const src = fs.readFileSync(path.join(DRIVERS_DIR, 'ours', 'edit-and-save.mjs'), 'utf8');
  assert.match(src, /async verify\(ctx\) \{/, 'the driver changed: update this plant');
  const planted = src.replace(/async verify\(ctx\) \{/, 'async verify(ctx, outcome) {\n    if (outcome === undefined) return { verified: false, details: {} };');
  assert.ok(lintDriver(planted).some(p => /second parameter/.test(p)), lintDriver(planted).join('\n'));
  // Without the outcome the plant has no way left to tell the calls apart (plants P1-P5 above), its
  // empty measured part is refused (P1, P2), its start form holds the number (P8) and no read
  // gains the number during the measured part (P7).
});
