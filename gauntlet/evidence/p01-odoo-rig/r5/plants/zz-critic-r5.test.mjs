// Critic r5 plants: faults planted in the measuring instrument's use. Each plant SHOULD end
// "invalid" (or its counts must not drop). A plant that ends "verified" with lower counts is MISSED.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout } from '../lib/runner.mjs';

// Captured when this module loads, as a driver module would capture it at its top level:
// run.mjs loads every driver (loadDriver) before the first execute() installs the network guard.
const fetchAtLoad = globalThis.fetch;

let server, base, tmp, things = 0;
const PAGE = `<!doctype html><html><body>
  <input id="q" aria-label="Query"><button id="go">Go</button><div id="out"></div>
  <script>
    document.getElementById('go').onclick = async () => {
      const r = await fetch('/api/slow?q=' + encodeURIComponent(document.getElementById('q').value));
      document.getElementById('out').textContent = 'found ' + (await r.text());
    };
  </script></body></html>`;
before(async () => {
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    if (u.pathname === '/api/slow') { setTimeout(() => { res.writeHead(200); res.end(u.searchParams.get('q') || ''); }, 2000); return; }
    if (u.pathname === '/api/things' && req.method === 'POST') { things++; res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end('{}'); }
    if (u.pathname === '/api/things') { res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ count: things })); }
    res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(PAGE);
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'critic-r5-'));
});
after(() => { server?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const standIn = () => ({ id: 'ours', baseUrl: base, users: {}, brandWords: [], readyKind: 'default' });
const runDriver = (driver, task = TASK) => execute(task, driver, standIn(), 'ours', {}, layout(path.join(tmp, String(Math.random()).slice(2))), { timeout: 10_000 });
const report = (name, r) => console.log(`RESULT ${name}: status=${r.status} steps=${r.counts?.steps} keys=${r.counts?.keystrokes} machine=${r.counts?.machine_seconds} wait=${r.counts?.system_wait_seconds} human=${r.counts?.human_seconds} error=${(r.error || '').split('\n')[0]}`);

test('control: honest path (type, Go, wait for the 2 s answer)', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'found abcdefghij', { label: 'result' }); return {}; },
    async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'found abcdefghij' }; },
  });
  report('control', r);
  assert.equal(r.status, 'verified');
  assert.ok(r.counts.machine_seconds >= 2);
});

test('plant T2 (wrong timer): run() returns right after the click; verify() waits for the end state off the clock', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); return {}; },
    async verify(ctx, outcome) {
      if (outcome === undefined) return { verified: (await ctx.page.locator('#out').textContent()) === 'found abcdefghij' };
      await ctx.page.locator('#out', { hasText: 'found abcdefghij' }).waitFor({ state: 'visible' });
      return { verified: true };
    },
  });
  report('T2', r);
  assert.notEqual(r.status, 'verified', `MISSED: verified with machine ${r.counts?.machine_seconds}s although the product took 2 s to answer`);
});

test('plant U4 (uncounted action): fetch captured at module load does the work inside the measured part', async () => {
  const before = things;
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.click('#q'); await fetchAtLoad(base + '/api/things', { method: 'POST' }); return {}; },
    async verify() { return { verified: (await (await fetch(base + '/api/things')).json()).count > before }; },
  });
  report('U4', r);
  assert.notEqual(r.status, 'verified', `MISSED: a back-end write through the module-load fetch went uncounted (steps ${r.counts?.steps})`);
});

test('plant U5 (uncounted action): process.getBuiltinModule reaches node:child_process / node:net while measured', async () => {
  const before = things;
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) {
      await op.click('#q');
      const cp = process.getBuiltinModule('node:child_process');
      // Asynchronous, so this test process (which also serves the stand-in product) keeps answering.
      await new Promise((resolve, reject) => cp.execFile('curl', ['-s', '-X', 'POST', base + '/api/things'], e => (e ? reject(e) : resolve())));
      return {};
    },
    async verify() { return { verified: (await (await fetch(base + '/api/things')).json()).count > before }; },
  });
  report('U5', r);
  assert.notEqual(r.status, 'verified', `MISSED: a back-end write through child_process went uncounted (steps ${r.counts?.steps})`);
});

for (const [name, chord, copyFirst] of [['C2 ControlOrMeta+v', 'ControlOrMeta+v', false], ['C3 empty copy then Control+v', 'Control+v', true]]) {
  test(`plant ${name}: text copied in set-up is pasted while measured`, async () => {
    const r = await runDriver({
      async signIn(ctx) {
        await ctx.page.goto(base + '/plant');
        await ctx.page.locator('#q').fill('abcdefghij');
        await ctx.page.locator('#q').press('Control+a');
        await ctx.page.locator('#q').press('Control+c');
        await ctx.page.locator('#q').fill('');
      },
      async run(op) {
        await op.click('#q');
        if (copyFirst) await op.press('Control+c'); // nothing is selected in the empty field: the clipboard keeps set-up's text
        await op.press(chord); await op.click('#go');
        await op.waitFor(() => document.getElementById('out').textContent === 'found abcdefghij', { timeout: 5000 });
        return {};
      },
      async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'found abcdefghij' }; },
    });
    report(name, r);
    assert.notEqual(r.status, 'verified', `MISSED: 10 characters entered with ${r.counts?.keystrokes} keys`);
  });
}
