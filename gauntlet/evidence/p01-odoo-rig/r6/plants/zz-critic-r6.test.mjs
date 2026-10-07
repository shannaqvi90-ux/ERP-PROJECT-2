// Critic r6 plants: faults planted against the measuring instrument, run the way the runner runs
// drivers (module files in the driver process). Each plant SHOULD end "invalid" (or not verified).
// A plant that ends "verified" with counts lower than the honest control is MISSED.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout } from '../lib/runner.mjs';
import { sandboxed } from './helpers/driver-module.mjs';

let posts = 0;
const PAGE = `<!doctype html><html><head><title>Stand-in</title></head><body>
  <input id="q" aria-label="Query"><button id="go">Go</button><button id="look">Look</button><div id="out"></div>
  <script>
    document.getElementById('go').onclick = async () => {
      const r = await fetch('/api/save', { method: 'POST', body: document.getElementById('q').value });
      document.getElementById('out').textContent = 'saved ' + (await r.text());
    };
    document.getElementById('look').onclick = async () => {
      const r = await fetch('/api/slow?q=' + encodeURIComponent(document.getElementById('q').value));
      document.getElementById('out').textContent = 'found ' + (await r.text());
    };
  </script></body></html>`;
let server, base, tmp;
before(async () => {
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    if (u.pathname === '/api/save') { let b = ''; req.on('data', c => { b += c; }); req.on('end', () => { posts++; setTimeout(() => { res.writeHead(200); res.end(b); }, 300); }); return; }
    if (u.pathname === '/api/slow') { setTimeout(() => { res.writeHead(200); res.end(u.searchParams.get('q') || ''); }, 2000); return; }
    if (u.pathname === '/api/count') { res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ posts })); }
    res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(PAGE);
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'critic-r6-'));
});
after(() => { server?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const standIn = () => ({ id: 'ours', baseUrl: base, users: {}, brandWords: [], readyKind: 'default' });
const runDriver = async (driver, scope = {}) => execute(TASK, await sandboxed(driver, { base, ...scope }), standIn(), 'ours', {}, layout(path.join(tmp, String(Math.random()).slice(2))), { timeout: 10_000 });
const report = (name, r) => console.log(`RESULT ${name}: status=${r.status} steps=${r.counts?.steps} keys=${r.counts?.keystrokes} machine=${r.counts?.machine_seconds} wait=${r.counts?.system_wait_seconds} human=${r.counts?.human_seconds} error=${(r.error || '').split('\n')[0]}`);

const VERIFY_SAVED = {
  async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' }; },
};

test('control: honest path (fill, Go, wait for the saved answer)', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'saved abcdefghij', { label: 'saved' }); return {}; },
    ...VERIFY_SAVED,
  });
  report('control', r);
  assert.equal(r.status, 'verified');
});

test('plant S1 (uncounted action): a ctx.read whose native-await continuation fills and clicks after the sentinel disarms', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op, ctx) {
      await ctx.read(() => {
        (async () => { await 0; const q = document.getElementById('q'); q.value = 'abcdefghij'; document.getElementById('go').click(); })();
        return 1;
      });
      await op.waitFor(() => document.getElementById('out').textContent === 'saved abcdefghij', { label: 'saved' });
      return {};
    },
    ...VERIFY_SAVED,
  });
  report('S1', r);
  assert.notEqual(r.status, 'verified', `MISSED: the task was done with ${r.counts?.steps} steps and ${r.counts?.keystrokes} keys`);
});

test('plant S2 (uncounted action): op.waitFor condition schedules the action through an animation finish callback', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) {
      await op.waitFor(() => {
        if (!window.__s2) { window.__s2 = 1; const a = document.body.animate([{ opacity: 1 }, { opacity: 1 }], 1); a.onfinish = () => { document.getElementById('q').value = 'abcdefghij'; document.getElementById('go').click(); }; }
        return document.getElementById('out').textContent === 'saved abcdefghij';
      }, { label: 'saved' });
      return {};
    },
    ...VERIFY_SAVED,
  });
  report('S2', r);
  assert.notEqual(r.status, 'verified', `MISSED: the task was done with ${r.counts?.steps} steps and ${r.counts?.keystrokes} keys`);
});

test('plant S3 (uncounted action): a MutationObserver callback registered by a read acts on the next DOM change', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op, ctx) {
      await ctx.read(() => {
        const mo = new MutationObserver(() => { mo.disconnect(); document.getElementById('q').value = 'abcdefghij'; document.getElementById('go').click(); });
        mo.observe(document.body, { attributes: true, subtree: true, childList: true });
        return 1;
      });
      await op.click('#q'); // one counted click triggers the planted work (focus ring / attribute change may not fire: fall back below)
      await op.waitFor(() => document.getElementById('out').textContent === 'saved abcdefghij', { label: 'saved', timeout: 4000 });
      return {};
    },
    ...VERIFY_SAVED,
  });
  report('S3', r);
  assert.notEqual(r.status, 'verified', `MISSED: the task was done with ${r.counts?.steps} steps and ${r.counts?.keystrokes} keys`);
});

test('plant T3 (wrong timer): run() returns after Look (a slow read); verify() polls the frozen page in short reads', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#look'); return {}; },
    async verify(ctx, outcome) {
      const done = async () => (await ctx.read(() => document.getElementById('out').textContent)) === 'found abcdefghij';
      if (outcome === undefined) return { verified: await done() };
      for (let i = 0; i < 9; i++) { if (await done()) return { verified: true }; await new Promise(r2 => setTimeout(r2, 300)); }
      return { verified: false };
    },
  });
  report('T3', r); console.log('T3 passes', JSON.stringify(r.verify_passes), 'after_clock', r.requests_after_clock);
  assert.notEqual(r.status, 'verified', `MISSED: verified with machine ${r.counts?.machine_seconds}s although the product took 2 s to answer`);
});
