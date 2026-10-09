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
import { execute, layout, compareRuns, containsValue, savedState, taskRuleProblems, VERIFY_PASS_GAP_MS } from '../lib/runner.mjs';
import { sandboxed, sandboxedSource } from './helpers/driver-module.mjs';

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

before(async () => {
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    if (u.pathname === '/api/save' && req.method === 'POST') { let b = ''; req.on('data', c => { b += c; }); req.on('end', () => { posts++; saved = b; res.writeHead(200); res.end(b); }); return; }
    if (u.pathname === '/api/mark' && req.method === 'POST') { version++; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/state') { res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ saved })); }
    if (u.pathname === '/api/version') { res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ version })); }
    if (u.pathname === '/api/now') { res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ now: Date.now() })); }
    res.writeHead(200, { 'Content-Type': 'text/html' });
    res.end(u.pathname === '/status' ? STATUS_PAGE() : PAGE());
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-before-clock-'));
});
after(() => { server?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
// The same task, declared as the product's back end holding its end state, and naming what the person enters.
const SAVES = { ...TASK, saves: true };
const ENTERS = { ...TASK, saves: true, input: { value: 'abcdefghij' }, enters: ['value'] };
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

test('plant P8: set-up saves the value the person is to enter and the start screen shows it -> refused at the start', async () => {
  reset();
  const r = await runDriver({
    async setup() { await fetch(base + '/api/save', { method: 'POST', body: 'abcdefghij' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.click('#go'); return {}; },
    async verify(ctx) { const s = await (await fetch(base + '/api/state')).json(); return { verified: s.saved === 'abcdefghij' }; },
  }, ENTERS);
  never(r, 'P8');
  assert.match(r.error, /start screen already shows "abcdefghij"/);
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
  const rec = (key, text) => ({ key: `GET http://x${key} `, digest: text, text });
  const t = ENTERS;
  assert.equal(savedState(t, [rec('/s', '{"saved":null}')], [rec('/s', '{"saved":"abcdefghij"}')], [rec('/s', '{"saved":"abcdefghij"}')]).problem, null);
  assert.match(savedState(t, [rec('/s', '{"saved":"abcdefghij"}')], [rec('/s', '{"saved":"abcdefghij"}')], [rec('/s', '{"saved":"abcdefghij"}')]).problem, /answered differently/);
  assert.match(savedState(t, [], [], []).problem, /read nothing/);
  // A read that differs between the two passes after the clock is a clock, not a saved state.
  assert.match(savedState(SAVES, [rec('/n', '1')], [rec('/n', '2')], [rec('/n', '3')]).problem, /answered differently/);
  // Read only after the clock: never compared, so never a change.
  assert.match(savedState(SAVES, [], [rec('/s', 'a')], [rec('/s', 'a')]).problem, /answered differently/);
  // Changed, but what the person enters was there before.
  assert.match(savedState(t, [rec('/v', '1'), rec('/s', 'abcdefghij')], [rec('/v', '2'), rec('/s', 'abcdefghij')], [rec('/v', '2'), rec('/s', 'abcdefghij')]).problem, /gained a value/);
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
