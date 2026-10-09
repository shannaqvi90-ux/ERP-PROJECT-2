// The harness paces its sign-ins under our product's limit and waits out a 429 (commit 3d9ff31,
// lib/sign-in-limit.mjs), through the sandboxed driver process (round 5, lib/sandbox/): a driver's
// API sign-in travels to the harness's fetch bridge and its browser sign-in is driven from the
// driver process, yet both are paced by the one budget of the harness process.
import test, { after, before, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, isLimitedSignIn, layout, verifyWaited } from '../lib/runner.mjs';
import { configureSignInLimit } from '../lib/sign-in-limit.mjs';
import { PRODUCTS } from '../lib/config.mjs';
import { sandboxed } from './helpers/driver-module.mjs';

// A stand-in product: a sign-in screen whose button posts to the sign-in address and shows the
// working screen on success, "limited" on a 429. The server refuses the next `refuse` sign-ins.
const PAGE = `<!doctype html><html><body><input id="q" aria-label="Query"><button id="in">Sign in</button><div id="out"></div>
  <script>document.getElementById('in').onclick = async () => {
    const r = await fetch('/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{"email":"a","password":"b"}' });
    document.getElementById('out').textContent = r.status === 200 ? 'working' : 'limited'; };</script></body></html>`;
let server, base, tmp;
const seen = [];
let refuse = 0;
before(async () => {
  server = http.createServer((req, res) => {
    if (req.url === '/api/auth/sign-in' && req.method === 'POST') {
      let b = '';
      req.on('data', c => { b += c; });
      req.on('end', () => {
        seen.push({ at: Date.now(), body: b });
        if (refuse > 0) { refuse--; res.writeHead(429); res.end('{"title":"too many sign-ins"}'); return; }
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ token: `t${seen.length}` }));
      });
      return;
    }
    if (req.url === '/api/me') { res.writeHead(200, { 'Content-Type': 'application/json' }); res.end('{"ok":true}'); return; }
    res.writeHead(200, { 'Content-Type': 'text/html' });
    res.end(PAGE);
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-sign-in-limit-'));
});
after(() => { server?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });
beforeEach(() => { seen.length = 0; refuse = 0; });

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const limited = () => ({ id: 'ours', baseUrl: base, users: {}, brandWords: [], readyKind: 'default', signInLimit: PRODUCTS.ours.signInLimit });
const run = async (driver, scope = {}) => execute(TASK, await sandboxed(driver, { base, ...scope }), limited(), 'ours', {},
  layout(path.join(tmp, String(Math.random()).slice(2))), { timeout: 10_000 });

test('our product declares its sign-in limit; Odoo has none', () => {
  assert.deepEqual({ ...PRODUCTS.ours.signInLimit }, { method: 'POST', path: '/api/auth/sign-in' });
  assert.equal(PRODUCTS.odoo.signInLimit, undefined);
  const response = (status, method, url) => ({ status: () => status, url: () => url, request: () => ({ method: () => method }) });
  assert.equal(isLimitedSignIn(PRODUCTS.ours.signInLimit, response(429, 'POST', `${base}/api/auth/sign-in`)), true);
  assert.equal(isLimitedSignIn(PRODUCTS.ours.signInLimit, response(429, 'GET', `${base}/api/auth/sign-in`)), false);
  assert.equal(isLimitedSignIn(PRODUCTS.ours.signInLimit, response(200, 'POST', `${base}/api/auth/sign-in`)), false);
  assert.equal(isLimitedSignIn(PRODUCTS.ours.signInLimit, response(429, 'POST', `${base}/api/other`)), false);
});

test('an API sign-in from the driver process is paced by the harness and waits out a 429', async () => {
  const restore = configureSignInLimit({ budget: 2, windowMs: 400 });
  try {
    refuse = 1;
    const r = await run({
      async setup(ctx) {
        // Three sessions of different users (sessions are reused per user): the third is over budget.
        for (const login of ['u1', 'u2', 'u3']) await oursAs(ctx.product, { login, password: 'p' });
      },
      async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
      async run(op) { await op.click('#q'); return {}; },
      async verify(ctx) { return { verified: (await ctx.read(() => document.activeElement?.id || '')) === 'q' }; },
    });
    assert.equal(r.status, 'verified', r.error);
    // u1 refused once (429), waited out, sent again; then u2 and u3 under a budget of 2 per window.
    assert.equal(seen.length, 4);
    assert.ok(seen[1].at - seen[0].at >= 400, `the retry waited out the window (${seen[1].at - seen[0].at} ms)`);
    assert.ok(seen[3].at - seen[1].at >= 380, `the third sign-in waited for the budget (${seen[3].at - seen[1].at} ms)`);
  } finally { restore(); }
});

test('a browser sign-in refused with 429 is tried again in a fresh context, after the abandoned one ended', async () => {
  const restore = configureSignInLimit({ budget: 24, windowMs: 300 });
  try {
    refuse = 1;
    const r = await run({
      async signIn(ctx) {
        await ctx.page.goto(base + '/plant');
        await ctx.page.locator('#in').click();
        // The working screen never comes after a 429: this attempt waits until its page closes.
        await ctx.until(() => document.getElementById('out').textContent === 'working', { timeout: 8_000 });
      },
      async run(op) { await op.click('#q'); return {}; },
      async verify(ctx) {
        return { verified: (await ctx.read(() => [document.activeElement?.id || '', document.getElementById('out').textContent])).join('|') === 'q|' };
      },
    });
    assert.equal(r.status, 'verified', r.error);
    assert.equal(seen.length, 2, 'one refused sign-in and one that was let in');
    assert.ok(seen[1].at - seen[0].at >= 300, 'the retry came after the window');
  } finally { restore(); }
});

test('a browser sign-in refused every time ends the run in an error, never a measurement', async () => {
  const restore = configureSignInLimit({ budget: 24, windowMs: 100 });
  try {
    refuse = 10;
    const r = await run({
      async signIn(ctx) {
        await ctx.page.goto(base + '/plant');
        await ctx.page.locator('#in').click();
        await ctx.until(() => document.getElementById('out').textContent === 'working', { timeout: 8_000 });
      },
      async run(op) { await op.click('#q'); return {}; },
      async verify() { return { verified: true }; },
    });
    assert.equal(r.status, 'error');
    assert.match(r.error, /refused the browser sign-in with 429 3 times/);
    assert.equal(r.counts, null);
  } finally { restore(); }
});

test('a sign-in the harness paces inside verify() is not charged to the pass (the meter reports it apart)', async () => {
  const restore = configureSignInLimit({ budget: 1, windowMs: 6_000 });
  try {
    const r = await run({
      async setup(ctx) { await oursAs(ctx.product, { login: 'setup', password: 'p' }); },
      async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
      async run(op) { await op.click('#q'); return {}; },
      async verify(ctx) {
        const api = await oursAs(ctx.product, { login: 'verifier', password: 'p' }); // paced: waits for the window
        return { verified: (await api.get('/api/me')).ok === true && (await ctx.read(() => document.activeElement?.id || '')) === 'q' };
      },
    });
    assert.equal(r.status, 'verified', r.error);
    // Round 9: every verify() call runs in a fresh process with the same arguments, so the first
    // call, before the clock, makes the paced sign-in; the calls after it reuse its answer.
    assert.ok(r.verify_before.paced_seconds >= 3, `the pacing is reported (${r.verify_before.paced_seconds} s)`);
    assert.ok(r.verify_before.seconds < 1, 'and not charged to the pass');
    assert.ok(r.verify_passes.every(p => p.paced_seconds === 0), 'the passes after the clock reuse the sign-in');
    // Without the deduction the same passes would read as waiting for the end state.
    const raw = [{ ...r.verify_before, seconds: r.verify_before.seconds + r.verify_before.paced_seconds }, r.verify_passes[1]];
    assert.match(verifyWaited(raw), /the first time/);
  } finally { restore(); }
});
