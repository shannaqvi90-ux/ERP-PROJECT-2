// Planted faults: drivers that act on the product without the operator. Each plant must end as
// "invalid" (never "verified"), and an honest driver on the same page must still verify. The
// last plants run the real `ours` sign-in driver, as written and with the round-2 critic's plant
// (password typed through ctx.page.keyboard), against a stand-in sign-in page.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { pathToFileURL } from 'node:url';
import { execute, layout } from '../lib/runner.mjs';
import { HARNESS_DIR } from '../lib/config.mjs';

const PAGE = `<!doctype html><html><head><title>Plant page</title></head><body>
  <input id="q" aria-label="Query"><button id="go" onclick="setTimeout(()=>{document.getElementById('out').textContent='found '+document.getElementById('q').value},50)">Go</button>
  <div id="out"></div><a id="deep" href="/deep">deep</a>
</body></html>`;

// Stand-in for our sign-in screen and the three API calls the ours sign-in driver makes.
const SIGN_IN = `<!doctype html><html><head><title>Sign in</title></head><body>
  <form id="f"><label>E-mail <input name="email" autofocus></label><label>Password <input name="password" type="password"></label><button type="submit">Sign in</button></form>
  <script>
    document.getElementById('f').addEventListener('submit', e => {
      e.preventDefault();
      const ok = e.target.email.value === 'signin.tester@demo-trading.example' && e.target.password.value === 'Sign-In-Pass-2026';
      if (ok) { document.cookie = 'sid=1; path=/'; location.href = '/home'; }
    });
  </script></body></html>`;
const HOME = '<!doctype html><html><body><nav aria-label="Main navigation">Home</nav></body></html>';

let server, base, tmp;
before(async () => {
  server = http.createServer((req, res) => {
    const json = v => { res.writeHead(200, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(v)); };
    if (req.url === '/api/auth/sign-in') return json({ token: 't' });
    if (req.url.startsWith('/api/identity/users')) return json({ items: [{ id: 'u1', email: 'signin.tester@demo-trading.example' }] });
    if (req.url === '/api/auth/session') {
      const signedIn = /sid=1/.test(req.headers.cookie || '');
      return json({ authenticated: signedIn, user: signedIn ? { id: 'u1', email: 'signin.tester@demo-trading.example' } : null });
    }
    if (req.url === '/api/things' && req.method === 'POST') return json({ id: 7 });
    res.writeHead(200, { 'Content-Type': 'text/html' });
    res.end(req.url === '/' ? SIGN_IN : req.url === '/home' ? HOME : PAGE);
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-guard-'));
});
after(() => { server?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });

const TASK = { id: 'plant', title: 'Plant', input: {} };
async function runDriver(driver, task = TASK) {
  const product = { id: 'ours', baseUrl: base, users: {}, brandWords: [] };
  return execute(task, driver, product, 'ours', {}, layout(path.join(tmp, String(Math.random()).slice(2))), {});
}

/** A driver whose run does `act` in the middle of an honest path. */
function planted(act, extra = {}) {
  return {
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op, ctx) {
      await op.fill('#q', 'abc', { label: 'query' });
      await act(op, ctx);
      await op.click('#go');
      await op.waitFor(() => document.getElementById('out').textContent.startsWith('found'), { label: 'result' });
      return {};
    },
    async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()).startsWith('found') }; },
    ...extra,
  };
}

test('control: an honest driver verifies; reading and locating are allowed while measured', async () => {
  const r = await runDriver(planted(async (op, ctx) => {
    assert.equal(await op.page.locator('#q').inputValue(), 'abc');
    assert.match(ctx.page.url(), /\/plant$/);
    assert.equal(await ctx.page.getByRole('button', { name: 'Go' }).count(), 1);
    assert.ok(await op.page.locator('#go').boundingBox());
  }));
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.counts.steps, 3);
  assert.equal(r.counts.keystrokes, 3);
});

const PLANTS = {
  'typing through ctx.page.keyboard': async (op, ctx) => { await ctx.page.keyboard.type('def'); },
  'pressing a key through op.page.keyboard': async op => { await op.page.keyboard.press('End'); },
  'clicking a locator directly': async op => { await op.page.locator('#go').click(); },
  'filling a field directly': async (op, ctx) => { await ctx.page.getByLabel('Query').fill('xyz'); },
  'the mouse': async (op, ctx) => { await ctx.page.mouse.click(5, 5); },
  'a deep link (goto)': async (op, ctx) => { await ctx.page.goto(base + '/deep'); },
  'a reload': async (op, ctx) => { await ctx.page.reload(); },
  'page script that clicks (evaluate)': async (op, ctx) => { await ctx.page.evaluate(() => document.getElementById('go').click()); },
  'locator script that clicks (locator.evaluate)': async op => { await op.page.locator('#go').evaluate(el => el.click()); },
  'a condition that clicks': async op => { await op.waitFor(() => { document.getElementById('go').click(); return true; }); },
  'a condition that sets a value': async op => { await op.waitFor(() => { document.getElementById('q').value = 'zzz'; return true; }); },
  'a condition that changes the page': async op => { await op.waitFor(() => { document.getElementById('out').textContent = 'found x'; return true; }); },
  'a condition that defers an action (setTimeout)': async op => { await op.waitFor(() => { setTimeout(() => document.getElementById('go').click(), 0); return true; }); },
  'an asynchronous condition': async op => { await op.waitFor(async () => true); },
  'a condition that navigates': async op => { await op.waitFor(() => { location.href = '/deep'; return true; }); },
  'a condition that calls the back end from the page': async op => { await op.waitFor(() => { fetch('/api/things', { method: 'POST' }); return true; }); },
  'a back-end call from the harness (fetch)': async () => { await fetch(base + '/api/things', { method: 'POST' }); },
  'a back-end call from the harness (http.request)': async () => {
    await new Promise((resolve, reject) => { const rq = http.request(base + '/api/things', { method: 'POST' }, resolve); rq.on('error', reject); rq.end(); });
  },
  'a second page in the same context': async (op, ctx) => { await ctx.context.newPage(); },
  'a new browser context': async (op, ctx) => { await ctx.browser.newContext(); },
  'the page request context (page.request)': async (op, ctx) => { await ctx.page.request.post(base + '/api/things'); },
  'reaching a browser-driver internal (_mainFrame)': async (op, ctx) => { await ctx.page._mainFrame.click('#go'); },
  'closing the page (Symbol.asyncDispose)': async (op, ctx) => { await ctx.page[Symbol.asyncDispose](); },
  'an action whose refusal the driver swallows': async (op, ctx) => { try { await ctx.page.keyboard.press('Enter'); } catch { /* ignored */ } },
};

for (const [name, act] of Object.entries(PLANTS)) {
  test(`plant: ${name} -> the run is invalid, never verified`, async () => {
    const r = await runDriver(planted(act));
    assert.equal(r.status, 'invalid', `${name}: ${r.status} ${r.error || ''}`);
    assert.match(r.error, /uncounted action/);
  });
}

test('plant: a keyboard kept from set-up and used while measured is refused', async () => {
  const r = await runDriver(planted(async (op, ctx) => { await ctx.state.keyboard.type('def'); }, {
    async setup(ctx) { ctx.state.keyboard = ctx.page.keyboard; },
  }));
  assert.equal(r.status, 'invalid', r.error);
});

test('plant: an internal channel kept from set-up is refused there too', async () => {
  let refused = null;
  const r = await runDriver(planted(async () => {}, {
    async setup(ctx) { try { ctx.state.channel = ctx.page._channel; } catch (e) { refused = e; } },
  }));
  assert.match(String(refused?.message), /internal of the browser driver/);
  assert.equal(r.status, 'verified', 'the honest rest of the run still verifies');
});

test('plant: a driver cannot stop the clock, drop steps or reach the raw page', async () => {
  let view;
  const r = await runDriver(planted(async op => {
    view = op;
    assert.equal(op.finish, undefined);
    assert.equal(op.start, undefined);
    assert.ok(Object.isFrozen(op));
    op.steps.pop();
    op.steps.length = 0;
  }));
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.counts.steps, 3, 'steps cannot be removed through the copies a driver sees');
  assert.ok(!('t0' in view) && !('summary' in view));
});

test('plant: browserKey refuses an action passed by the driver', async () => {
  const r = await runDriver(planted(async op => { await op.browserKey('F5', page => page.keyboard.type('x')); }));
  assert.notEqual(r.status, 'verified');
  assert.match(r.error, /browserKey/);
});

test('plant: the guard controls cannot be claimed by a driver', async () => {
  const guard = await import('../lib/guard.mjs');
  assert.throws(() => guard.claimClock(), /already held/);
  assert.throws(() => guard.claimViolations(), /already held/);
});

test('outside the measured part the same guarded page does everything (set-up, verification)', async () => {
  const r = await runDriver({
    async setup(ctx) {
      await ctx.page.goto(base + '/plant');
      await ctx.page.locator('#q').fill('pre');
      await ctx.page.keyboard.press('End');
      assert.equal(await ctx.page.evaluate(() => document.getElementById('q').value), 'pre');
    },
    async run(op) { await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'found pre'); return {}; },
    async verify(ctx) {
      await ctx.page.locator('#q').fill('post');
      return { verified: await ctx.page.evaluate(() => document.getElementById('q').value) === 'post' };
    },
  });
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.counts.steps, 1);
});

// The real ours sign-in driver, as written and planted.
const OURS_SIGN_IN = path.join(HARNESS_DIR, 'drivers', 'ours', 'sign-in.mjs');
async function loadVariant(transform) {
  const libUrl = pathToFileURL(path.join(HARNESS_DIR, 'lib')).href;
  const src = transform(fs.readFileSync(OURS_SIGN_IN, 'utf8')).replaceAll("'../../lib/", `'${libUrl}/`);
  const file = path.join(tmp, `sign-in-${Math.random().toString(36).slice(2)}.mjs`);
  fs.writeFileSync(file, src);
  return (await import(pathToFileURL(file).href)).default;
}
const SIGN_IN_TASK = { id: 'sign-in', title: 'Sign in', input: { user: 'signin.tester@demo-trading.example', password: 'Sign-In-Pass-2026', name: 'Sara Signin' } };
const signInProduct = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'a', password: 'b' } }, brandWords: [] });

/** Every expert path of a driver, the way runTask runs them. */
async function executeAll(driver, dir) {
  const variants = driver.variants ? Object.entries(driver.variants) : [[null, {}]];
  const out = [];
  for (const [id, v] of variants) out.push({ id, ...(await execute(SIGN_IN_TASK, { ...driver, ...v }, signInProduct(), 'ours', {}, layout(path.join(tmp, `${dir}-${id}`)), {})) });
  return out;
}

test('the real ours sign-in driver verifies on a stand-in sign-in page', async () => {
  const runs = await executeAll(await loadVariant(s => s), 'si-ok');
  assert.ok(runs.length >= 1);
  for (const r of runs) {
    assert.equal(r.status, 'verified', `${r.id}: ${r.error}`);
    assert.equal(r.counts.steps, 4, `${r.id}: the stand-in remembers nothing, so every path types the e-mail`);
  }
});

test("plant H1 (round 2): the ours sign-in driver types the password through ctx.page.keyboard -> invalid", async () => {
  const driver = await loadVariant(s => {
    const planted = s.replace(/await op\.type\(password[^\n]*\n\s*await op\.press\('Enter'[^\n]*\n/,
      "await ctx.page.keyboard.type(password);\n    await ctx.page.keyboard.press('Enter');\n");
    assert.notEqual(planted, s, 'the plant must change the driver');
    return planted;
  });
  for (const r of await executeAll(driver, 'si-plant')) assert.equal(r.status, 'invalid', `${r.id}: ${r.status} ${r.error}`);
});

test('an API task counts each request, and its screenshots show the neutral request transcript', async () => {
  const task = { id: 'api-plant', title: 'API plant', channel: 'api', input: {} };
  const r = await runDriver({
    async signIn(ctx) { ctx.useApi({ baseUrl: base, headers: { Authorization: 'Bearer t' } }); },
    async run(op) { const res = await op.request('POST', '/api/things', { name: 'x' }); return { id: res.body.id }; },
    async verify(ctx, outcome) { return { verified: outcome.id === 7 }; },
  }, task);
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.counts.steps, 1);
  assert.equal(r.counts.requests, 1);
  assert.equal(r.counts.keystrokes, 'POST /api/things {"name":"x"}'.length + 4 /* P, O, S, T */ + 4 /* " x4 */ + 1 /* : */ + 2 /* { } */ + 1 /* Enter */);
  assert.equal(r.screenshots.length, 2);
});

test('plant: signing in to the API inside the measured part is refused', async () => {
  const task = { id: 'api-plant', title: 'API plant', channel: 'api', input: {} };
  const r = await runDriver({
    async run(op, ctx) { ctx.useApi({ baseUrl: base }); await op.request('POST', '/api/things', {}); return {}; },
    async verify() { return { verified: true }; },
  }, task);
  assert.equal(r.status, 'invalid', r.error);
});
