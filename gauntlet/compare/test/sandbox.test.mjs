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

// Round 9: verify() runs in a process of its own, so the run's process is examined in set-up and
// what it found travels to verify() in ctx.state (as data).
test('inside the driver process: no child process, no worker, no writes outside the scratch folder, no socket', async () => {
  const r = await run({
    async setup(ctx) {
      const p = process.permission;
      const tmpdir = os.tmpdir();
      const scratchFile = path.join(tmpdir, 'ok.txt');
      fs.writeFileSync(scratchFile, 'x');
      let socket = null;
      try { process.getBuiltinModule('node:net').connect(9, '127.0.0.1'); socket = 'opened'; } catch (e) { socket = e.name; }
      ctx.state.details = {
        child: p.has('child'), worker: p.has('worker'), writeHarness: p.has('fs.write', ctx.harnessDir), writeScratch: p.has('fs.write', tmpdir),
        readHarness: p.has('fs.read', ctx.harnessDir), scratchWritten: fs.readFileSync(scratchFile, 'utf8') === 'x', socket, pid: process.pid,
      };
    },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.fill('#q', 'abc'); return {}; },
    async verify(ctx) { return { verified: (await ctx.page.locator('#q').inputValue()) === 'abc', details: ctx.state.details }; },
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

test('inside the driver process: every way to open a socket is refused by its own lock (round 7, critic mutation M4)', async () => {
  const r = await run({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.fill('#q', 'abc'); return {}; },
    async verify(ctx) {
      const net = process.getBuiltinModule('node:net');
      const tls = process.getBuiltinModule('node:tls');
      const http = process.getBuiltinModule('node:http');
      const dgram = process.getBuiltinModule('node:dgram');
      const tries = {
        'new net.Socket().connect': () => new net.Socket().connect(9, '127.0.0.1'),
        'net.connect': () => net.connect(9, '127.0.0.1'),
        'net.createConnection': () => net.createConnection(9, '127.0.0.1'),
        'net.Server#listen': () => net.createServer().listen(0),
        'tls.connect': () => tls.connect(9, '127.0.0.1'),
        'http.request': () => http.request('http://127.0.0.1:9/'),
        'dgram.Socket#send': () => dgram.createSocket('udp4').send('x', 9, '127.0.0.1'),
      };
      const out = {};
      for (const [k, f] of Object.entries(tries)) { try { f(); out[k] = 'opened'; } catch (e) { out[k] = String(e.message); } }
      return { verified: (await ctx.page.locator('#q').inputValue()) === 'abc', details: out };
    },
  });
  assert.equal(r.status, 'invalid');
  const d = r.verification.details;
  // Each attempt is refused by the lock on the very call it makes, not by a later layer.
  assert.match(d['new net.Socket().connect'], /net\.Socket#connect/);
  assert.match(d['net.connect'], /net\.connect\b/);
  assert.match(d['net.createConnection'], /net\.createConnection/);
  assert.match(d['net.Server#listen'], /net\.Server#listen/);
  assert.match(d['tls.connect'], /tls\.connect/);
  assert.match(d['http.request'], /http\.request/);
  assert.match(d['dgram.Socket#send'], /dgram\.Socket#send/);
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
      // The listener (in this process, the run's) saw the page's one POST.
      ctx.page.off('request', ctx.state.listener);
      const posts = ctx.state.posts;
      assert.equal(posts.length, 1, JSON.stringify(posts));
      assert.match(posts[0].url, /\/api\/echo$/);
      assert.equal(posts[0].body, 'abc');
      return {};
    },
    async verify(ctx) {
      // Round 9: the listener (a function) stays in the run's process; verify() gets set-up's state as data.
      return { verified: (await ctx.page.locator('#out').textContent()) === 'found abc', details: { listener: ctx.state.listener ?? null, posts: ctx.state.posts } };
    },
  });
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.verification.details.listener, null, 'a function never reaches verify()');
  assert.deepEqual(r.verification.details.posts, [], 'verify() gets the state as it stood at the start');
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
    async verify(ctx) { return { verified: (await ctx.read(() => document.activeElement?.id || '')) === 'q' }; },
  }, { base }), standIn(), 'ours', {}, layout(path.join(tmp, 'hang')), { timeout: 1_000, hookTimeout: 3_000 });
  assert.notEqual(r.status, 'verified');
  assert.match(r.error, /did not answer/);
  const next = await run({ async signIn(ctx) { await ctx.page.goto(base + '/plant'); }, async run(op) { await op.click('#q'); return {}; }, async verify(ctx) { return { verified: (await ctx.read(() => document.activeElement?.id || '')) === 'q' }; } });
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
      ctx.state.pid = process.pid;
      await ctx.page.goto(base + '/plant');
    },
    async run(op) { await op.click('#q'); return {}; },
    async verify(ctx) { return { verified: (await ctx.read(() => document.activeElement?.id || '')) === 'q', details: { pid: ctx.state.pid } }; },
  });
  const next = await run({
    async signIn(ctx) {
      // What the run's process (this one) holds, examined before the measured part.
      Object.assign(ctx.state, { pid: process.pid, poisoned: globalThis.__poisoned ?? null, patched: Promise.prototype.then.toString().includes('slow') });
      await ctx.page.goto(base + '/plant');
    },
    async run(op) { await op.click('#q'); await new Promise(r => setTimeout(r, 10)); return {}; },
    async verify(ctx) {
      return { verified: (await ctx.read(() => document.activeElement?.id || '')) === 'q', details: { pid: ctx.state.pid, poisoned: ctx.state.poisoned, patched: ctx.state.patched } };
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

// Round 9 (critic p01 r8): verify() runs in a fresh process for each call, with the same arguments
// before and after the clock. That process runs nothing else, reads only the harness's code and
// data and the measured part's downloads, and has no clock.
test('a verify() process: one per call, reads only the harness and the downloads, writes nothing, no clock, nothing of the run\'s process', async () => {
  const r = await run({
    async setup(ctx) {
      // A mark the run's process leaves where verify() might look for it.
      fs.writeFileSync(path.join(os.tmpdir(), 'mark.txt'), 'ran');
      ctx.state.runScratch = os.tmpdir();
      ctx.state.runPid = process.pid;
      ctx.state.setUpAt = Date.now();
      globalThis.__fromRun = true;
    },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.fill('#q', 'abc'); return {}; },
    async verify(ctx) {
      const tryRead = f => { try { f(); return 'read'; } catch (e) { return e.code || e.name; } };
      const details = {
        pid: process.pid,
        role: process.env.COMPARE_DRIVER_ROLE,
        args: arguments.length,
        fromRun: globalThis.__fromRun ?? null,
        runMark: tryRead(() => fs.readFileSync(path.join(ctx.state.runScratch, 'mark.txt'))),
        tmpListing: tryRead(() => fs.readdirSync(path.dirname(os.tmpdir()))),
        proc: tryRead(() => fs.readFileSync('/proc/uptime')),
        harnessLib: tryRead(() => fs.readFileSync(path.join(ctx.harnessDir, 'lib', 'config.mjs'))),
        harnessRuns: tryRead(() => fs.readdirSync(path.join(ctx.harnessDir, 'runs'))),
        now: Date.now(), dateNow: new Date().getTime(), perf: performance.now(), uptime: process.uptime(), osUptime: os.uptime(),
        hr: process.hrtime.bigint().toString(), setUpAt: ctx.state.setUpAt, protoNow: Object.getPrototypeOf(performance).now.call(performance),
        write: tryRead(() => fs.writeFileSync(path.join(os.tmpdir(), 'w.txt'), 'x')),
        intl: new Intl.DateTimeFormat('en', { timeStyle: 'medium', timeZone: 'UTC' }).format() === new Intl.DateTimeFormat('en', { timeStyle: 'medium', timeZone: 'UTC' }).format(new Date()),
        downloads: ctx.downloads,
        dateHeader: (await fetch(base + '/plant')).headers.get('date'),
      };
      return { verified: (await ctx.page.locator('#q').inputValue()) === 'abc', details };
    },
  });
  assert.equal(r.status, 'verified', r.error);
  const d = r.verification.details;
  assert.equal(d.role, 'verify');
  assert.equal(d.args, 1, 'verify() gets ctx alone, before and after the clock');
  assert.notEqual(d.pid, process.pid);
  assert.equal(d.fromRun, null, 'nothing of the run\'s process reaches verify()');
  assert.equal(d.runMark, 'ERR_ACCESS_DENIED', 'the run\'s scratch folder is out of reach');
  assert.equal(d.tmpListing, 'ERR_ACCESS_DENIED');
  assert.equal(d.proc, 'ERR_ACCESS_DENIED');
  assert.equal(d.harnessLib, 'read');
  assert.equal(d.harnessRuns, 'ERR_ACCESS_DENIED', 'the results and shots of runs are out of reach');
  // The clocks stand at the moment the run began, before set-up.
  assert.equal(d.now, d.dateNow);
  assert.ok(d.now <= d.setUpAt, `the clock of verify() (${d.now}) stands before set-up (${d.setUpAt})`);
  assert.equal(d.perf, 0);
  assert.equal(d.uptime, 0);
  assert.equal(d.osUptime, 0);
  assert.equal(d.hr, '0');
  assert.equal(d.protoNow, 0, 'the prototype\'s clock stands still too');
  assert.equal(d.write, 'ERR_ACCESS_DENIED', 'a verify() process writes nothing (a file\'s time is a clock)');
  assert.equal(d.intl, true);
  assert.deepEqual(d.downloads, []);
  assert.equal(d.dateHeader, null, 'the product\'s Date header (a clock) never reaches verify()');
  assert.equal(r.verify_before.verified, false);
  assert.equal(r.verify_passes.length, 2);
  // Each verify() call had a process of its own, gone with the call.
  assert.throws(() => process.kill(d.pid, 0), /ESRCH/);
});
