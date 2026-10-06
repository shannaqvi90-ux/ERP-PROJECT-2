// The driver sandbox (lib/sandbox/, round 5): drivers run in a process of their own, under Node's
// permission model and the network lockdown, and reach the products only through the harness.
// These tests check the sandbox itself from inside a driver, and that the stand-in objects a driver
// holds there behave like the Playwright objects they stand for.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout } from '../lib/runner.mjs';
import { HARNESS_DIR } from '../lib/config.mjs';
import { DriverHost, hostArgs } from '../lib/sandbox/bridge.mjs';
import { loadDriver } from '../lib/registry.mjs';
import { sandboxed } from './helpers/driver-module.mjs';

const PAGE = `<!doctype html><html><body><input id="q" aria-label="Query"><button id="go">Go</button><div id="out"></div><a id="next" href="/next">next</a>
  <script>document.getElementById('go').onclick = async () => {
    const r = await fetch('/api/echo', { method: 'POST', body: document.getElementById('q').value });
    document.getElementById('out').textContent = 'found ' + (await r.text()); };</script></body></html>`;
let server, base, tmp;
before(async () => {
  server = http.createServer((req, res) => {
    if (req.url === '/api/echo') { let b = ''; req.on('data', c => { b += c; }); req.on('end', () => { res.writeHead(200); res.end(b); }); return; }
    res.writeHead(200, { 'Content-Type': 'text/html' });
    res.end(PAGE);
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-sandbox-'));
});
after(() => { server?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const standIn = () => ({ id: 'ours', baseUrl: base, users: {}, brandWords: [], readyKind: 'default' });
const run = async (driver, scope = {}) => execute(TASK, await sandboxed(driver, { base, ...scope }), standIn(), 'ours', {}, layout(path.join(tmp, String(Math.random()).slice(2))), { timeout: 10_000 });

test('the driver process starts under the permission model with the network lockdown preloaded', () => {
  const args = hostArgs('/tmp/x');
  assert.ok(args.includes('--permission'));
  assert.ok(args.includes('--allow-fs-write=/tmp/x'));
  assert.ok(!args.some(a => /^--allow-(child-process|worker|addons|wasi|inspector)/.test(a)), 'no capability beyond reading and its scratch folder');
  assert.ok(args.includes('--import') && args[args.indexOf('--import') + 1].endsWith(path.join('sandbox', 'lockdown.mjs')));
});

test('inside the driver process: no child process, no worker, no writes outside the scratch folder, no socket', async () => {
  const r = await run({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.click('#q'); return {}; },
    async verify(ctx, outcome) {
      if (outcome === undefined) return { verified: false };
      const p = process.permission;
      const tmpdir = os.tmpdir();
      const scratchFile = path.join(tmpdir, 'ok.txt');
      fs.writeFileSync(scratchFile, 'x');
      let socket = null;
      try { process.getBuiltinModule('node:net').connect(9, '127.0.0.1'); socket = 'opened'; } catch (e) { socket = e.name; }
      return {
        verified: true,
        details: {
          child: p.has('child'), worker: p.has('worker'), writeHarness: p.has('fs.write', ctx.harnessDir), writeScratch: p.has('fs.write', tmpdir),
          readHarness: p.has('fs.read', ctx.harnessDir), scratchWritten: fs.readFileSync(scratchFile, 'utf8') === 'x', socket, pid: process.pid,
        },
      };
    },
  });
  // The socket attempt in verify() is refused and reported: the run is invalid, and its details say why.
  assert.equal(r.status, 'invalid');
  assert.match(r.error, /network connection/);
  const d = r.verification.details;
  assert.equal(d.child, false);
  assert.equal(d.worker, false);
  assert.equal(d.writeHarness, false);
  assert.equal(d.writeScratch, true);
  assert.equal(d.readHarness, true);
  assert.equal(d.scratchWritten, true);
  assert.equal(d.socket, 'UncountedAction');
  assert.notEqual(d.pid, process.pid, 'the driver ran in another process');
});

test('the stand-ins a driver holds behave like Playwright objects: sync reads, locators, listeners, ready', async () => {
  const r = await run({
    ready: page => page.locator('#go'),
    async signIn(ctx) {
      await ctx.page.goto(base + '/plant');
      assert.match(ctx.page.url(), /\/plant$/);
      assert.equal(ctx.page.viewportSize().width, 1600);
      assert.equal(ctx.page.isClosed(), false);
      assert.equal(typeof ctx.page.context().cookies, 'function');
      assert.match(String(ctx.page.locator('#q').first()), /Page/);
      await ctx.page.locator('a#next').click();
      await ctx.page.waitForURL('**/next');
      assert.match(ctx.page.url(), /\/next$/, 'the address follows navigation');
      await ctx.page.goto(base + '/plant');
    },
    observe(ctx) {
      ctx.state.posts = [];
      ctx.state.listener = r => { if (r.method() === 'POST') ctx.state.posts.push({ url: r.url(), body: r.postData(), type: r.resourceType() }); };
      ctx.page.on('request', ctx.state.listener);
    },
    async run(op, ctx) {
      await op.fill('#q', 'abc');
      await op.click(op.page.getByRole('button', { name: 'Go' }));
      await op.waitFor(op.page.locator('#out', { hasText: 'found abc' }));
      assert.equal(await op.page.locator('#q').inputValue(), 'abc');
      assert.equal(op.steps.length, 3, 'the step copies follow each action');
      return { posts: ctx.state.posts.length };
    },
    async verify(ctx, outcome) {
      if (outcome === undefined) return { verified: false };
      ctx.page.off('request', ctx.state.listener);
      return { verified: (await ctx.page.locator('#out').textContent()) === 'found abc', details: { posts: ctx.state.posts, outcome: outcome ?? null } };
    },
  });
  assert.equal(r.status, 'verified', r.error);
  const posts = r.verification.details.posts;
  assert.equal(posts.length, 1, JSON.stringify(posts));
  assert.match(posts[0].url, /\/api\/echo$/);
  assert.equal(posts[0].body, 'abc');
  assert.equal(r.counts.steps, 3);
});

test('the harness reads drivers and tasks through the driver process and never imports them', async () => {
  const d = await loadDriver('ours', 'sign-in');
  assert.equal(d.hooks.run || Object.values(d.variants || {}).every(v => v.run), true);
  assert.equal(d.file, path.join(HARNESS_DIR, 'drivers', 'ours', 'sign-in.mjs'));
  // No driver or task module is in this process's module graph: they are read in the driver process.
  const { default: mod } = await import('node:module');
  assert.ok(!Object.keys(mod._cache || {}).some(k => k.includes(`${path.sep}drivers${path.sep}`)));
  assert.ok(DriverHost.shared().child.pid !== process.pid);
});

test('a driver process that stops answering is stopped, and the next run gets a new one', async () => {
  const r = await execute(TASK, await sandboxed({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run() { for (;;) { /* never answers */ } },
    async verify(ctx, outcome) { return { verified: outcome !== undefined }; },
  }, { base }), standIn(), 'ours', {}, layout(path.join(tmp, 'hang')), { timeout: 1_000, hookTimeout: 3_000 });
  assert.notEqual(r.status, 'verified');
  assert.match(r.error, /did not answer/);
  const next = await run({ async signIn(ctx) { await ctx.page.goto(base + '/plant'); }, async run(op) { await op.click('#q'); return {}; }, async verify(ctx, outcome) { return { verified: outcome !== undefined }; } });
  assert.equal(next.status, 'verified', next.error);
});

test('plant X1 (round 6): every run has a driver process of its own, so a driver cannot slow or steer the next run (the other product\'s included)', async () => {
  // A driver that patches its process's globals: timers ten times slower, every promise late, a
  // marker for the next run. In a shared process the next driver (Odoo's, with --product both)
  // would be slowed on the clock.
  const poison = await run({
    async signIn(ctx) {
      const slow = globalThis.setTimeout;
      globalThis.setTimeout = (fn, ms, ...a) => slow(fn, (ms || 0) * 10 + 500, ...a);
      const then = Promise.prototype.then;
      Promise.prototype.then = function (a, b) { return then.call(this, v => new Promise(r => slow(() => r(v), 50)).then(a), b); };
      globalThis.__poisoned = process.pid;
      await ctx.page.goto(base + '/plant');
    },
    async run(op) { await op.click('#q'); return {}; },
    async verify(ctx, outcome) { return { verified: outcome !== undefined, details: { pid: process.pid } }; },
  });
  const next = await run({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.click('#q'); await new Promise(r => setTimeout(r, 10)); return {}; },
    async verify(ctx, outcome) {
      return { verified: outcome !== undefined, details: { pid: process.pid, poisoned: globalThis.__poisoned ?? null, patched: Promise.prototype.then.toString().includes('slow') } };
    },
  });
  assert.equal(next.status, 'verified', next.error);
  assert.notEqual(next.verification.details.pid, poison.verification?.details?.pid ?? -1, 'the next run had a process of its own');
  assert.equal(next.verification.details.poisoned, null);
  assert.equal(next.verification.details.patched, false);
  assert.ok(next.counts.machine_seconds < 1, `machine ${next.counts.machine_seconds}: the next run was not slowed`);
  // The run's process ends with its run.
  const pid = next.verification.details.pid;
  assert.throws(() => process.kill(pid, 0), /ESRCH/);
});
