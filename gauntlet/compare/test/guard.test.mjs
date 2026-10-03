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
import { loadNeedles } from '../data/generate.mjs';

const PAGE = `<!doctype html><html><head><title>Plant page</title></head><body>
  <input id="q" aria-label="Query"><button id="go" onclick="setTimeout(()=>{document.getElementById('out').textContent='found '+document.getElementById('q').value},50)">Go</button>
  <div id="out"></div><a id="deep" href="/deep">deep</a>
</body></html>`;

// Stand-in for our sign-in screen, home screen, users list and the API calls the ours drivers make.
// The session is a cookie the sign-in screen sets; signing out revokes it on the server.
const SIGN_IN = `<!doctype html><html><head><title>Sign in</title></head><body>
  <form id="f"><label>E-mail <input name="email" autofocus></label><label>Password <input name="password" type="password"></label><button type="submit">Sign in</button></form>
  <script>
    document.getElementById('f').addEventListener('submit', e => {
      e.preventDefault();
      const ok = e.target.email.value === 'signin.tester@demo-trading.example' && e.target.password.value === 'Sign-In-Pass-2026';
      if (ok) { document.cookie = 'sid=' + Math.random().toString(36).slice(2) + '; path=/'; location.href = '/'; }
    });
  </script></body></html>`;
const HOME = '<!doctype html><html><body><nav aria-label="Main navigation"><a href="/users">Users</a></nav><main><h1>Home</h1></main></body></html>';
const usersPage = users => `<!doctype html><html><body><nav aria-label="Main navigation"><a href="/users">Users</a></nav>
  <main><input type="search" aria-label="Search users" id="s"><table><tbody id="rows"></tbody></table><aside id="panel" hidden></aside></main>
  <script>
    const users = ${JSON.stringify(users)};
    const rows = document.getElementById('rows');
    const draw = q => { rows.innerHTML = ''; for (const u of users.filter(x => !q || x.name.includes(q))) {
      const tr = document.createElement('tr'); tr.innerHTML = '<td></td><td></td>'; tr.cells[0].textContent = u.name; tr.cells[1].textContent = u.login;
      tr.onclick = () => { const p = document.getElementById('panel'); p.hidden = false; p.innerHTML = '<dl><dt>Name</dt><dd></dd><dt>Sign-in</dt><dd></dd></dl>';
        p.querySelectorAll('dd')[0].textContent = u.name; p.querySelectorAll('dd')[1].textContent = u.login; };
      rows.append(tr); } };
    draw('');
    document.getElementById('s').addEventListener('input', e => setTimeout(() => draw(e.target.value), 30));
  </script></body></html>`;
// A screen that keeps what was typed into its field in a cookie and shows it again on load (a
// product that remembers a search): the start check must see it.
const REMEMBERING = `<!doctype html><html><head><title>Plant page</title></head><body><input id="q" aria-label="Query"><button id="go">Go</button><div id="out"></div>
  <script>
    const m = /(?:^|; )q=([^;]*)/.exec(document.cookie); if (m) document.getElementById('q').value = decodeURIComponent(m[1]);
    document.getElementById('q').addEventListener('input', e => { document.cookie = 'q=' + encodeURIComponent(e.target.value) + '; path=/'; });
    document.getElementById('go').onclick = () => setTimeout(() => { document.getElementById('out').textContent = 'found ' + document.getElementById('q').value; }, 50);
  </script></body></html>`;

let server, base, tmp, needles;
const revoked = new Set();
let things = 0;
const sid = req => (/(?:^|; )sid=([^;]+)/.exec(req.headers.cookie || '') || [])[1];
before(async () => {
  needles = loadNeedles();
  const users = [
    { name: 'Sara Signin', login: 'signin.tester@demo-trading.example' },
    { name: needles.user.name, login: needles.user.login },
    { name: 'Omar Other', login: 'omar.other@demo-trading.example' },
  ];
  server = http.createServer((req, res) => {
    const json = v => { res.writeHead(200, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(v)); };
    const html = h => { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(h); };
    const signedIn = !!sid(req) && !revoked.has(sid(req));
    if (req.url === '/api/auth/sign-in') return json({ token: 't' });
    if (req.url === '/api/auth/sign-out') { if (sid(req)) revoked.add(sid(req)); res.writeHead(204); return res.end(); }
    if (req.url.startsWith('/api/identity/users')) {
      const q = decodeURIComponent(new URL(req.url, 'http://x').searchParams.get('search') || '').toLowerCase();
      return json({ items: users.filter(u => !q || u.login.toLowerCase().includes(q) || u.name.toLowerCase().includes(q)).map((u, i) => ({ id: `u${i}`, email: u.login, displayName: u.name })) });
    }
    if (req.url === '/api/auth/session') return json({ authenticated: signedIn, user: signedIn ? { id: 'u0', email: 'signin.tester@demo-trading.example' } : null });
    if (req.url === '/api/things' && req.method === 'POST') { things++; return json({ id: 7 }); }
    if (req.url === '/api/things') return json({ count: things });
    if (req.url === '/') return html(signedIn ? HOME : SIGN_IN);
    if (req.url === '/users') return html(usersPage(users));
    if (req.url.startsWith('/remembering')) return html(REMEMBERING);
    html(PAGE);
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-guard-'));
});
after(() => { server?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });

// The plant task starts on a screen its sign-in opens ('list'): the runner reloads that screen in a
// fresh browser context. Plant runs time out after 10 s instead of 2 minutes.
const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const standIn = () => ({ id: 'ours', baseUrl: base, users: {}, brandWords: [], readyKind: 'default' });
async function runDriver(driver, task = TASK) {
  return execute(task, driver, standIn(), 'ours', {}, layout(path.join(tmp, String(Math.random()).slice(2))), { timeout: 10_000 });
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
  // Instrument 4: a refusal at any point of the run, set-up included, invalidates it.
  assert.equal(r.status, 'invalid', r.error);
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
  assert.throws(() => guard.claimPhase(), /already held/);
});

test('set-up may act on the product but never run page script; verify() only reads', async () => {
  const r = await runDriver({
    async setup(ctx) {
      await ctx.page.goto(base + '/plant');
      await ctx.page.locator('#q').fill('pre');
      await ctx.page.keyboard.press('End');
      assert.equal(await ctx.read(() => document.getElementById('q').value), 'pre', 'ctx.read reads the page in set-up');
      await ctx.until(() => document.getElementById('q').value === 'pre', { timeout: 2000 });
    },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.fill('#q', 'abc'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'found abc'); return {}; },
    async verify(ctx) {
      return { verified: await ctx.read(() => document.getElementById('q').value) === 'abc' && (await ctx.page.locator('#out').textContent()) === 'found abc' };
    },
  });
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.counts.steps, 3);
  assert.equal(r.start_state.kind, 'list');
  assert.deepEqual(r.start_state.filled, [], 'the start screen was reloaded: nothing typed during set-up is on it');
});

// ---------------------------------------------------------------------------------------------
// Round 3 plants: acting outside the clock, and shortcuts a driver declared for itself. Each must
// end "invalid" (or fail honestly), never "verified" with lower counts.

/** The honest path on the plant page: click the field, type, click Go: 3 steps. */
const honestRun = async op => { await op.fill('#q', 'abc'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'found abc', { label: 'result' }); return {}; };
const verifyFound = async ctx => ({ verified: (await ctx.page.locator('#out').textContent()) === 'found abc' });

test('plant U1 (round 3): page script installed during sign-in to act in the measured part -> invalid', async () => {
  let refused = null;
  const r = await runDriver({
    async signIn(ctx) {
      await ctx.page.goto(base + '/plant');
      // The critic's plant: a listener that fills the field and clicks Go when the measured part types.
      try {
        await ctx.page.evaluate(() => document.getElementById('q').addEventListener('input', () => setTimeout(() => document.getElementById('go').click(), 20), { once: true }));
      } catch (e) { refused = e; }
    },
    async run(op) { await op.click('#q'); await op.type('abc'); await op.waitFor(() => document.getElementById('out').textContent === 'found abc', { timeout: 3000 }); return {}; },
    verify: verifyFound,
  });
  assert.match(String(refused?.message), /action outside the clock/);
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
});

test('plant U1b (round 3): a listener installed through a locator in set-up -> invalid', async () => {
  const r = await runDriver({
    async signIn(ctx) {
      await ctx.page.goto(base + '/plant');
      try { await ctx.page.locator('#q').evaluate(el => el.addEventListener('input', () => document.getElementById('go').click())); } catch { /* swallowed */ }
    },
    run: honestRun,
    verify: verifyFound,
  });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
});

test('plant U2 (round 3): an init script added to the context in set-up (an F9 that does the task) -> invalid', async () => {
  const r = await runDriver({
    async setup(ctx) {
      try {
        await ctx.context.addInitScript(() => document.addEventListener('keydown', e => {
          if (e.key === 'F9') { document.getElementById('q').value = 'abc'; document.getElementById('go').click(); }
        }));
      } catch { /* swallowed */ }
    },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.press('F9'); await op.waitFor(() => document.getElementById('out').textContent === 'found abc', { timeout: 3000 }); return {}; },
    verify: verifyFound,
  });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
});

test('plant U2b (round 3): exposed functions, routes and extra headers made in set-up -> invalid', async () => {
  for (const act of [
    ctx => ctx.page.exposeFunction('doTask', () => 1),
    ctx => ctx.context.route('**/api/**', route => route.fulfill({ body: '{}' })),
    ctx => ctx.page.setExtraHTTPHeaders({ 'X-Shortcut': '1' }),
    ctx => ctx.page.clock.install(),
  ]) {
    const r = await runDriver({ async signIn(ctx) { await ctx.page.goto(base + '/plant'); try { await act(ctx); } catch { /* swallowed */ } }, run: honestRun, verify: verifyFound });
    assert.equal(r.status, 'invalid', `${act}: ${r.status} ${r.error}`);
  }
});

test('plant U3 (round 3): even a script that escaped the guard would not survive: the start page is a fresh context', async () => {
  // The guard refuses page script, but the fresh start does not rely on it: whatever the set-up
  // page holds (its listeners, its typed text) is gone. Here set-up types into the field (allowed)
  // and the run, trusting it, only clicks Go: the reloaded screen has an empty field, so it fails.
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); await ctx.page.locator('#q').fill('abc'); },
    async run(op) { await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'found abc', { timeout: 2000 }); return {}; },
    verify: verifyFound,
  });
  assert.notEqual(r.status, 'verified', 'typed text from sign-in reached the measured part');
  assert.deepEqual(r.start_state.filled, []);
});

test('plant H2 (round 3): the start state is checked: a field the product refills from what sign-in typed -> invalid', async () => {
  // The screen remembers its search in a cookie; sign-in types the search, so the start screen
  // would open with it filled in and one step fewer to measure.
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/remembering'); await ctx.page.locator('#q').fill('abc'); },
    async run(op) { await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'found abc'); return {}; },
    verify: verifyFound,
  });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.match(r.error, /unfair start state/);
});

test('plant H2b (round 3): a start screen that carries state in its address -> invalid', async () => {
  const r = await runDriver({ async signIn(ctx) { await ctx.page.goto(base + '/plant?q=abc'); }, run: honestRun, verify: verifyFound });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.match(r.error, /carries a query/);
});

test('plant H2c (round 3): a task already done by set-up before the clock -> invalid', async () => {
  const task = { ...TASK, id: 'plant-done' };
  const r = await runDriver({
    async setup() { await fetch(base + '/api/things', { method: 'POST' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.waitFor(() => true); return {}; },
    async verify() { return { verified: (await (await fetch(base + '/api/things')).json()).count > 0 }; },
  }, task);
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.match(r.error, /already done before the clock/);
});

test('plant V1 (round 3): verify() that acts on the page -> invalid', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.fill('#q', 'abc'); return {}; },
    async verify(ctx) { await ctx.page.locator('#go').click(); await ctx.page.locator('#out', { hasText: 'found' }).waitFor(); return verifyFound(ctx); },
  });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.match(r.error, /action outside the clock \(verifying\)/);
});

test('plant V2 (round 3): verify() that changes the product through the back end -> invalid', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.click('#q'); return {}; },
    async verify() { await fetch(base + '/api/things', { method: 'POST' }); return { verified: true }; },
  });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.match(r.error, /changes the product/);
});

test('plant S1 (round 3): a newline inside op.type (Enter without a step) -> invalid', async () => {
  const r = await runDriver(planted(async op => { await op.type('d\n'); }));
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.match(r.error, /control character/);
});

test('plant K1 (round 3): a driver that declares chain to drop mental preparation -> invalid', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) {
      await op.click('#q', { chain: true }); await op.type('abc', { chain: true }); await op.click('#go', { chain: true });
      await op.waitFor(() => document.getElementById('out').textContent === 'found abc');
      return {};
    },
    verify: verifyFound,
  });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.match(r.error, /derived by the instrument/);
});

test('plant T1 (round 3): screenshots while the product works neither hide its time nor go undeclared', async () => {
  // A slow product: Go answers after 1.5 s.
  const slow = { ...TASK, id: 'plant-slow', moments: ['result'] };
  const mk = shots => ({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) {
      await op.fill('#q', 'abc');
      await op.click('#go');
      await shots(op);
      await op.waitFor(() => document.getElementById('out').textContent === 'found abc');
      await op.shot('result');
      return {};
    },
    verify: verifyFound,
  });
  // The critic's plant: shots in a loop while the product works -> refused (not a declared moment).
  const gamed = await runDriver(mk(async op => { for (let i = 0; i < 5; i++) await op.shot('progress'); }), slow);
  assert.equal(gamed.status, 'invalid', `${gamed.status} ${gamed.error}`);
  assert.match(gamed.error, /does not declare/);
  // A declared moment shot while the product works stays on the clock.
  const early = await runDriver({ ...mk(async op => { await op.shot('result'); }), async run(op) {
    await op.fill('#q', 'abc'); await op.click('#go'); await op.shot('result');
    await op.waitFor(() => document.getElementById('out').textContent === 'found abc');
    return {};
  } }, slow);
  assert.equal(early.status, 'verified', early.error);
  const lastStep = early.steps[early.steps.length - 1];
  assert.ok(early.counts.machine_seconds >= lastStep.at + lastStep.took + 0.05 - 0.002, 'the product time after the last step is on the clock');
  // A declared moment the driver never shoots -> invalid (every driver pays for the same shots).
  const skipped = await runDriver({ ...mk(async () => {}), async run(op) { await honestRun(op); return {}; } }, slow);
  assert.equal(skipped.status, 'invalid', `${skipped.status} ${skipped.error}`);
  assert.match(skipped.error, /were not shot/);
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
const SIGN_IN_TASK = { id: 'sign-in', title: 'Sign in', startAt: 'sign-in', moments: [], input: { user: 'signin.tester@demo-trading.example', password: 'Sign-In-Pass-2026', name: 'Sara Signin' } };
const signInProduct = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'a', password: 'b' } }, brandWords: [] });

/** Every expert path of a driver, the way runTask runs them. */
async function executeAll(driver, dir) {
  const variants = driver.variants ? Object.entries(driver.variants) : [[null, {}]];
  const out = [];
  for (const [id, v] of variants) out.push({ id, ...(await execute(SIGN_IN_TASK, { ...driver, ...v }, signInProduct(), 'ours', {}, layout(path.join(tmp, `${dir}-${id}`)), { timeout: 15_000 })) });
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

test('plant K1 (round 3, the real driver): the ours sign-in driver with every step declared chained -> invalid', async () => {
  const driver = await loadVariant(s => {
    // The critic's plant (gauntlet/evidence/p01-odoo-rig/r3/plants/plant-K1-chain-sign-in.diff), on the driver as it stands.
    const planted = s.replace("{ label: 'e-mail' }", "{ label: 'e-mail', chain: true }")
      .replace("{ label: 'next field (password)' }", "{ label: 'next field (password)', chain: true }")
      .replace("{ label: 'password' }", "{ label: 'password', chain: true }")
      .replace("{ label: 'sign in' }", "{ label: 'sign in', chain: true }");
    assert.notEqual(planted, s, 'the plant must change the driver');
    return planted;
  });
  for (const r of await executeAll(driver, 'si-k1')) {
    assert.equal(r.status, 'invalid', `${r.id}: ${r.status} ${r.error}`);
    assert.match(r.error, /derived by the instrument/);
  }
});

// The real ours find-user driver on a stand-in users screen.
const OURS_FIND_USER = path.join(HARNESS_DIR, 'drivers', 'ours', 'find-user.mjs');
async function loadFindUser(transform) {
  const libUrl = pathToFileURL(path.join(HARNESS_DIR, 'lib')).href;
  const src = transform(fs.readFileSync(OURS_FIND_USER, 'utf8')).replaceAll("'../../lib/", `'${libUrl}/`);
  const file = path.join(tmp, `find-user-${Math.random().toString(36).slice(2)}.mjs`);
  fs.writeFileSync(file, src);
  return (await import(pathToFileURL(file).href)).default;
}
const FIND_USER_TASK = { id: 'find-user', title: 'Find one user', startAt: 'home', moments: ['result list'], input: {} };
const findUserProduct = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'signin.tester@demo-trading.example', password: 'Sign-In-Pass-2026' } }, brandWords: [] });
const runFindUser = driver => execute(FIND_USER_TASK, driver, findUserProduct(), 'ours', needles, layout(path.join(tmp, `fu-${Math.random().toString(36).slice(2)}`)), { timeout: 8_000 });

test('the real ours find-user driver verifies on a stand-in users screen', async () => {
  const r = await runFindUser(await loadFindUser(s => s));
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.counts.steps, 4, 'Users, the search box, the name, the row');
  assert.equal(r.start_state.kind, 'home');
  assert.equal(r.start_state.path, '/');
});

test('plant H2 (round 3, the real driver): ours find-user signs in, opens Users and types the name before the clock -> never a 1-step win', async () => {
  const driver = await loadFindUser(s => {
    // The critic's plant (gauntlet/evidence/p01-odoo-rig/r3/plants/plant-H2-start-state-find-user.diff), on the driver as it stands.
    const planted = s.replace("    await usersLink(page).waitFor();\n  },", `    await usersLink(page).waitFor();
    await usersLink(page).click();
    await searchBox(page).fill(ctx.needles.user.name);
    await page.getByRole('row').filter({ hasText: ctx.needles.user.name }).first().waitFor();
  },`).replace(/\n    await op\.click\(usersLink\(op\.page\)[^\n]*\n    await op\.waitFor\(searchBox[^\n]*\n    await op\.fill\(searchBox[^\n]*/, '');
    assert.equal((planted.match(/searchBox\(page\)\.fill/g) || []).length, 1, 'the plant must move the search into sign-in');
    assert.doesNotMatch(planted, /op\.fill\(searchBox/, 'the plant must take the search out of the measured part');
    return planted;
  });
  const r = await runFindUser(driver);
  assert.notEqual(r.status, 'verified', `the planted driver verified with ${r.counts?.steps} steps`);
  assert.equal(r.start_state?.path, '/', 'the measured part starts on the screen after sign-in, whatever sign-in opened');
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
