// Planted faults: drivers that act on the product without the operator. Each plant must end as
// "invalid" (never "verified"), and an honest driver on the same page must still verify. The
// last plants run the real `ours` sign-in driver, as written and with the round-2 critic's plant
// (password typed through ctx.page.keyboard), against a stand-in sign-in page.
//
// Round 5: drivers run only in the sandboxed driver process (lib/sandbox/). A test's driver is
// written as a module (test/helpers/driver-module.mjs); the names it uses from this file (the
// stand-in's address `base`, a planted action ...) are passed to it as constants.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout } from '../lib/runner.mjs';
import { describeDriverFile } from '../lib/registry.mjs';
import { SCOPE, plantedFile, sandboxed, sandboxedSource } from './helpers/driver-module.mjs';
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

const SLOW_PAGE = `<!doctype html><html><body><input id="q" aria-label="Query"><button id="go">Go</button><div id="out"></div>
  <script>document.getElementById('go').onclick = async () => {
    const r = await fetch('/api/slow?q=' + encodeURIComponent(document.getElementById('q').value));
    document.getElementById('out').textContent = 'found ' + (await r.text()); };</script></body></html>`;
const DEBOUNCE_PAGE = `<!doctype html><html><body><input id="q" aria-label="Query"><button id="go">Go</button><div id="out"></div>
  <script>document.getElementById('go').onclick = () => setTimeout(async () => {
    const r = await fetch('/api/echo?q=' + encodeURIComponent(document.getElementById('q').value));
    document.getElementById('out').textContent = 'found ' + (await r.text()); }, 600);</script></body></html>`;
const ASYNC_PAGE = `<!doctype html><html><body><button id="save">Save</button><div id="out"></div>
  <script>document.getElementById('save').onclick = async () => { await fetch('/api/async-save', { method: 'POST' }); document.getElementById('out').textContent = 'accepted'; };</script></body></html>`;
const NAV_FORM = '<!doctype html><html><body><form action="/nav-list"><input name="go" id="go" aria-label="Go to" autofocus></form></body></html>';
const NAV_LIST = '<!doctype html><html><body><input id="s" aria-label="Search" autofocus><div id="out"></div><script>document.getElementById("s").addEventListener("input", e => { document.getElementById("out").textContent = "found " + e.target.value; });</script></body></html>';
let asyncSaved = false;
// A save answered at once and committed 1.5 s later. Its timer can outlive the test that clicked
// Save, so each test resets the state and cancels saves still pending from an earlier test.
const asyncSaveTimers = new Set();
const resetAsyncSave = () => { for (const t of asyncSaveTimers) clearTimeout(t); asyncSaveTimers.clear(); asyncSaved = false; };
let curlRequests = 0;
let echoHits = 0;
let apiUsers = [];

let server, base, tmp, needles;
const revoked = new Set();
let things = 0;
// Round 4 stand-ins: a form whose Enter saves on the server, the screen that shows what was saved,
// and a home preference that makes the home address open another screen.
let saved = '';
let homePreference = null;
const FORM = '<!doctype html><html><body><form method="post" action="/form"><input name="v" id="v" aria-label="Value" autofocus></form></body></html>';
const sid = req => (/(?:^|; )sid=([^;]+)/.exec(req.headers.cookie || '') || [])[1];
before(async () => {
  needles = loadNeedles();
  apiUsers = [{ id: 'u1', email: needles.user.login, displayName: needles.user.name, language: 'en', isActive: true, roleIds: [], version: 1 }];
  const users = [
    { name: 'Sara Signin', login: 'signin.tester@demo-trading.example' },
    { name: needles.user.name, login: needles.user.login },
    { name: 'Omar Other', login: 'omar.other@demo-trading.example' },
  ];
  server = http.createServer((req, res) => {
    const json = v => { res.writeHead(200, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(v)); };
    const html = h => { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(h); };
    const signedIn = !!sid(req) && !revoked.has(sid(req));
    // Round 5 stand-ins: a search answered after 2 s, a search sent 600 ms after the click, a save
    // answered at once but committed 1.5 s later, and the users API of the real api-update-user driver.
    const u5 = new URL(req.url, 'http://x');
    if (/curl/i.test(req.headers['user-agent'] || '')) curlRequests++;
    if (u5.pathname === '/api/slow') { setTimeout(() => { res.writeHead(200); res.end(u5.searchParams.get('q') || ''); }, 2000); return; }
    if (u5.pathname === '/api/slow-things' && req.method === 'POST') { setTimeout(() => { things++; json({ id: 8 }); }, 1500); return; }
    if (u5.pathname === '/api/echo') { echoHits++; }
    if (u5.pathname === '/api/echo') { res.writeHead(200); return res.end(u5.searchParams.get('q') || ''); }
    if (u5.pathname === '/api/async-save' && req.method === 'POST') { const t = setTimeout(() => { asyncSaveTimers.delete(t); asyncSaved = true; }, 1500); asyncSaveTimers.add(t); return json({ accepted: true }); }
    if (u5.pathname === '/api/async-saved') return json({ saved: asyncSaved });
    if (u5.pathname === '/slow-page') return html(SLOW_PAGE);
    if (u5.pathname === '/save-page') return html('<!doctype html><html><body><button id="save" onclick="fetch(\'/api/slow-things\', { method: \'POST\' })">Save</button></body></html>');
    if (u5.pathname === '/images-page') return html('<!doctype html><html><body><input id="q" aria-label="Query"><button id="go">Go</button><div id="out"></div><script>document.getElementById("q").addEventListener("click", () => { const i = new Image(); i.src = "/slow-image"; document.body.append(i); fetch("/api/slow?q=x"); });</script></body></html>');
    if (u5.pathname === '/slow-image') { setTimeout(() => { res.writeHead(200, { 'Content-Type': 'image/gif' }); res.end(Buffer.from('R0lGODlhAQABAAAAACw=', 'base64')); }, 2000); return; }
    if (u5.pathname === '/debounce-page') return html(DEBOUNCE_PAGE);
    if (u5.pathname === '/async-page') return html(ASYNC_PAGE);
    if (u5.pathname === '/nav-form') return html(NAV_FORM);
    if (u5.pathname === '/nav-list') return html(NAV_LIST);
    const apiUser = /^\/api\/identity\/users\/(u\d+)$/.exec(u5.pathname);
    if (apiUser && req.method === 'PUT' && req.headers.authorization === 'Bearer api') {
      let body = '';
      req.on('data', c => { body += c; });
      req.on('end', () => { const b = JSON.parse(body || '{}'); const u = apiUsers.find(x => x.id === apiUser[1]); if (u) { u.language = b.language; u.version++; } json(u || {}); });
      return;
    }
    if (apiUser && req.headers.authorization === 'Bearer api') return json(apiUsers.find(x => x.id === apiUser[1]) || {});
    if (u5.pathname === '/api/identity/users' && req.headers.authorization === 'Bearer api') {
      const q = (u5.searchParams.get('search') || '').toLowerCase();
      return json({ items: apiUsers.filter(x => !q || x.email.toLowerCase().includes(q) || x.displayName.toLowerCase().includes(q)) });
    }
    if (req.url === '/api/auth/sign-in') {
      let body = '';
      req.on('data', c => { body += c; });
      req.on('end', () => json({ token: /"email":"api\.tester"/.test(body) ? 'api' : 't' }));
      return;
    }
    if (req.url === '/api/auth/sign-out') { if (sid(req)) revoked.add(sid(req)); res.writeHead(204); return res.end(); }
    if (req.url.startsWith('/api/identity/users')) {
      const q = decodeURIComponent(new URL(req.url, 'http://x').searchParams.get('search') || '').toLowerCase();
      return json({ items: users.filter(u => !q || u.login.toLowerCase().includes(q) || u.name.toLowerCase().includes(q)).map((u, i) => ({ id: `u${i}`, email: u.login, displayName: u.name })) });
    }
    if (req.url === '/api/auth/session') return json({ authenticated: signedIn, user: signedIn ? { id: 'u0', email: 'signin.tester@demo-trading.example' } : null });
    if (req.url === '/api/things' && req.method === 'POST') { things++; return json({ id: 7 }); }
    if (req.url === '/api/things') return json({ count: things });
    if (req.url === '/form' && req.method === 'POST') {
      let body = '';
      req.on('data', c => { body += c; });
      req.on('end', () => { saved = new URLSearchParams(body).get('v') || ''; html('<!doctype html><p>saved</p>'); });
      return;
    }
    if (req.url === '/form') return html(FORM);
    if (req.url === '/saved') return html(`<!doctype html><html><body><div id="out">saved ${saved.replace(/[<&]/g, '')}</div></body></html>`);
    if (req.url === '/api/home-preference' && req.method === 'POST') { homePreference = '/users'; return json({}); }
    if (req.url === '/' && homePreference) { res.writeHead(302, { Location: homePreference }); return res.end(); }
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
async function runDriver(driver, task = TASK, scope = {}) {
  return execute(task, await sandboxed(driver, { base, ...scope }), standIn(), 'ours', {}, layout(path.join(tmp, String(Math.random()).slice(2))), { timeout: 10_000 });
}

/** A driver whose run does `act` in the middle of an honest path. */
function planted(act, extra = {}) {
  return {
    [SCOPE]: { act },
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
  const r = await runDriver(planted(async () => {}, {
    async setup(ctx) {
      let refused = null;
      try { ctx.state.channel = ctx.page._channel; } catch (e) { refused = e; }
      assert.match(String(refused?.message), /internal of the browser driver/);
    },
  }));
  assert.match(String(r.error), /internal of the browser driver/);
  // Instrument 4: a refusal at any point of the run, set-up included, invalidates it.
  assert.equal(r.status, 'invalid', r.error);
});

test('plant: a driver cannot stop the clock, drop steps or reach the raw page', async () => {
  const r = await runDriver(planted(async op => {
    assert.equal(op.finish, undefined);
    assert.equal(op.start, undefined);
    assert.ok(Object.isFrozen(op));
    assert.ok(!('t0' in op) && !('summary' in op) && !('settle' in op));
    op.steps.pop();
    op.steps.length = 0;
  }));
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.counts.steps, 3, 'steps cannot be removed through the copies a driver sees');
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
  const r = await runDriver({
    async signIn(ctx) {
      await ctx.page.goto(base + '/plant');
      // The critic's plant: a listener that fills the field and clicks Go when the measured part types.
      try {
        await ctx.page.evaluate(() => document.getElementById('q').addEventListener('input', () => setTimeout(() => document.getElementById('go').click(), 20), { once: true }));
      } catch (e) { ctx.state.refused = e; }
      assert.match(String(ctx.state.refused?.message), /action outside the clock/);
    },
    async run(op) { await op.click('#q'); await op.type('abc'); await op.waitFor(() => document.getElementById('out').textContent === 'found abc', { timeout: 3000 }); return {}; },
    verify: verifyFound,
  });
  assert.match(String(r.error), /action outside the clock/);
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
    const r = await runDriver({ async signIn(ctx) { await ctx.page.goto(base + '/plant'); try { await act(ctx); } catch { /* swallowed */ } }, run: honestRun, verify: verifyFound }, TASK, { act });
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
    [SCOPE]: { shots },
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
  const early = await runDriver({ ...mk(async op => { await op.shot('result'); }), [SCOPE]: { shots: null }, async run(op) {
    await op.fill('#q', 'abc'); await op.click('#go'); await op.shot('result');
    await op.waitFor(() => document.getElementById('out').textContent === 'found abc');
    return {};
  } }, slow);
  assert.equal(early.status, 'verified', early.error);
  const lastStep = early.steps[early.steps.length - 1];
  assert.ok(early.counts.machine_seconds >= lastStep.at + lastStep.took + 0.05 - 0.002, 'the product time after the last step is on the clock');
  // A declared moment the driver never shoots -> invalid (every driver pays for the same shots).
  const skipped = await runDriver({ ...mk(async () => {}), async run(op) { await honestRun(op); return {}; } }, slow, { honestRun });
  assert.equal(skipped.status, 'invalid', `${skipped.status} ${skipped.error}`);
  assert.match(skipped.error, /were not shot/);
});

// The real ours sign-in driver, as written and planted.
const OURS_SIGN_IN = path.join(HARNESS_DIR, 'drivers', 'ours', 'sign-in.mjs');
function loadVariant(transform) {
  return describeDriverFile(plantedFile(OURS_SIGN_IN, transform));
}
const SIGN_IN_TASK = { id: 'sign-in', title: 'Sign in', startAt: 'sign-in', moments: [], input: { user: 'signin.tester@demo-trading.example', password: 'Sign-In-Pass-2026', name: 'Sara Signin' } };
const signInProduct = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'a', password: 'b' } }, brandWords: [] });

/** Every expert path of a driver, the way runTask runs them. */
async function executeAll(driver, dir) {
  const variants = driver.variants ? Object.entries(driver.variants) : [[null, {}]];
  const out = [];
  for (const [id] of variants) out.push({ id, ...(await execute(SIGN_IN_TASK, { ...driver, variant: id }, signInProduct(), 'ours', {}, layout(path.join(tmp, `${dir}-${id}`)), { timeout: 15_000 })) });
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
function loadFindUser(transform) {
  return describeDriverFile(plantedFile(OURS_FIND_USER, transform));
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

// ---------------------------------------------------------------------------------------------
// Round 4: nothing set-up leaves behind may act or count inside the measured part.

test('plant P1 (round 4): an action left pending in a second browser context from set-up -> never verified', async () => {
  // Set-up opens its own context and starts typing slowly into a form whose Enter saves on the
  // server; it returns at once, so the typing would finish inside the measured part, uncounted. The
  // runner closes every set-up context at the start, so the pending typing dies with it.
  saved = '';
  const r = await runDriver({
    async setup(ctx) {
      const side = await ctx.browser.newContext();
      const p = await side.newPage();
      await p.goto(base + '/form');
      p.locator('#v').pressSequentially('abc\n', { delay: 700 }).catch(() => {});
    },
    async signIn(ctx) { await ctx.page.goto(base + '/saved'); },
    async run(op) {
      await op.browserKey('F5', { label: 'reload' });
      await op.waitFor(() => document.getElementById('out')?.textContent === 'saved abc', { label: 'saved', timeout: 4000 });
      return {};
    },
    async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abc' }; },
  });
  assert.notEqual(r.status, 'verified', `the pending set-up typing did the task: ${r.counts?.steps} step(s)`);
  assert.equal(r.start_state?.set_up_contexts_closed, 1, 'the set-up context the driver opened was closed at the start');
  assert.equal(saved, '', 'the pending set-up typing reached the server after the start');
});

test('plant P2 (round 4): launching another browser in set-up -> invalid', async () => {
  const r = await runDriver({
    async setup(ctx) { await ctx.browser.browserType().launch(); },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    run: honestRun,
    verify: verifyFound,
  });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.match(r.error, /BrowserType\.launch/);
});

test('plant P3 (round 4): a browser-wide debugging session or trace from set-up -> invalid', async () => {
  for (const act of [ctx => ctx.browser.newBrowserCDPSession(), ctx => ctx.browser.startTracing()]) {
    const r = await runDriver({ async setup(ctx) { await act(ctx); }, async signIn(ctx) { await ctx.page.goto(base + '/plant'); }, run: honestRun, verify: verifyFound }, TASK, { act });
    assert.equal(r.status, 'invalid', `${act}: ${r.status} ${r.error}`);
  }
});

test('plant L1 (round 4): set-up changes where the product opens (a home preference) -> invalid', async () => {
  // The home address now opens the users list: a home start must land on the product's home.
  const task = { ...TASK, id: 'plant-home', startAt: 'home' };
  try {
    const r = await runDriver({
      async setup() { await fetch(base + '/api/home-preference', { method: 'POST' }); },
      async signIn(ctx) { await ctx.page.goto(base + '/'); },
      // The users list is already open: search, open the row (the Users click is skipped).
      async run(op) {
        await op.fill('#s', needles.user.name, { label: 'name' });
        const row = op.page.locator('#rows tr', { hasText: needles.user.name }).first();
        await op.waitFor(row, { label: 'row' });
        await op.click(row, { label: 'open the user' });
        await op.waitFor('#panel:not([hidden])', { label: 'record' });
        return {};
      },
      async verify(ctx) { return { verified: (await ctx.page.locator('#panel:not([hidden])').count()) === 1 && (await ctx.page.locator('#panel').innerText()).includes(needles.user.login) }; },
    }, task, { needles });
    assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
    assert.match(r.error, /home start landed on \/users/);
    assert.equal(r.start_state.path, '/users');
  } finally {
    homePreference = null;
  }
});

test('plant L2 (round 4): a list start whose address names the task\'s data -> invalid', async () => {
  // The search is typed into the address before the clock (a path, so the query check alone misses it).
  for (const where of [`/users/${encodeURIComponent(needles.user.name)}`, `/users/by-name/${needles.user.name.toLowerCase().replace(/ /g, '-')}`]) {
    const r = await execute(TASK, await sandboxed({
      async signIn(ctx) { await ctx.page.goto(base + where); },
      run: honestRun,
      verify: verifyFound,
    }, { base, where }), standIn(), 'ours', needles, layout(path.join(tmp, String(Math.random()).slice(2))), { timeout: 10_000 });
    assert.equal(r.status, 'invalid', `${where}: ${r.status} ${r.error}`);
    assert.match(r.error, /names the task's data/);
  }
});

test('control (round 4): a home start that lands on the home, with set-up contexts of its own, still verifies', async () => {
  const task = { ...TASK, id: 'plant-home-ok', startAt: 'home' };
  const r = await runDriver({
    async setup(ctx) { const side = await ctx.browser.newContext(); await (await side.newPage()).goto(base + '/form'); },
    async signIn(ctx) { await ctx.context.addCookies([{ name: 'sid', value: 'control', url: base }]); await ctx.page.goto(base + '/'); },
    async run(op) { await op.click('a[href="/users"]', { label: 'Users' }); await op.waitFor('#s', { label: 'users list' }); return {}; },
    async verify(ctx) { return { verified: await ctx.page.locator('#s').isVisible() }; },
  }, task);
  assert.equal(r.status, 'verified', `${r.status} ${r.error}`);
  assert.equal(r.start_state.path, '/');
  assert.equal(r.start_state.set_up_contexts_closed, 1);
});

test('plant C1 (round 4): text copied to the clipboard in set-up and pasted while measured -> invalid', async () => {
  // Set-up types the value and copies it (allowed then: it is set-up); the measured part would
  // paste it with two keys instead of typing it.
  for (const chord of ['Control+v', 'Meta+v', 'Shift+Insert', 'Control+Shift+v']) {
    const r = await runDriver({
      async signIn(ctx) {
        await ctx.page.goto(base + '/plant');
        await ctx.page.locator('#q').fill('abc');
        await ctx.page.locator('#q').press('Control+a');
        await ctx.page.locator('#q').press('Control+c');
        await ctx.page.locator('#q').fill('');
      },
      async run(op) { await op.click('#q'); await op.press(chord); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'found abc', { timeout: 3000 }); return {}; },
      verify: verifyFound,
    }, TASK, { chord });
    assert.equal(r.status, 'invalid', `${chord}: ${r.status} ${r.error}`);
    assert.match(r.error, /not copied inside the measured part/);
  }
});

test('control (round 4): text copied inside the measured part may be pasted', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) {
      await op.fill('#q', 'abc'); await op.press('Control+a'); await op.press('Control+c');
      await op.press('Control+v'); await op.click('#go');
      await op.waitFor(() => document.getElementById('out').textContent.startsWith('found'), { timeout: 3000 });
      return {};
    },
    async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()).startsWith('found') }; },
  });
  assert.equal(r.status, 'verified', `${r.status} ${r.error}`);
});

// ---------------------------------------------------------------------------------------------
// Round 5: nothing a driver does in its own Node process may act uncounted or off the clock. The
// critic's plants (gauntlet/evidence/p01-odoo-rig/r5/plants/zz-critic-r5.test.mjs) first, then
// further ways out of the driver process. Each must end "invalid", or, where the product's answer
// is put on the clock instead, with counts no lower than the honest path's.

const SLOW_RESULT = () => document.getElementById('out').textContent === 'found abcdefghij';
const slowSignIn = async ctx => { await ctx.page.goto(base + '/slow-page'); };

test('control (round 5): the honest path on the slow page waits for the 2 s answer', async () => {
  const r = await runDriver({
    signIn: slowSignIn,
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'found abcdefghij', { label: 'result' }); return {}; },
    async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'found abcdefghij' }; },
  });
  assert.equal(r.status, 'verified', r.error);
  assert.ok(r.counts.machine_seconds >= 2, `machine ${r.counts.machine_seconds}`);
  assert.equal(r.verify_passes.length, 2, 'verify() runs twice and both passes are recorded');
});

test('plant T2 (round 5): run() returns after the click and verify() waits for the end state -> invalid', async () => {
  const r = await runDriver({
    signIn: slowSignIn,
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); return {}; },
    async verify(ctx, outcome) {
      if (outcome === undefined) return { verified: (await ctx.page.locator('#out').textContent()) === 'found abcdefghij' };
      await ctx.page.locator('#out', { hasText: 'found abcdefghij' }).waitFor({ state: 'visible' });
      return { verified: true };
    },
  });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error} machine ${r.counts?.machine_seconds}`);
  assert.match(r.error, /never waits/);
});

test('plant T2b (round 5): run() returns after the click and verify() reads once -> the screen is frozen as the clock stopped', async () => {
  const r = await runDriver({
    signIn: slowSignIn,
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); return {}; },
    async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'found abcdefghij' }; },
  });
  assert.notEqual(r.status, 'verified', `verified in ${r.counts?.machine_seconds} s, under the product's 2 s answer`);
  // Even later: the page's script stays frozen, so the answer that arrives never reaches the screen.
  const late = await runDriver({
    signIn: slowSignIn,
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); return {}; },
    async verify(ctx, outcome) {
      if (outcome !== undefined) { const t = Date.now(); while (Date.now() - t < 900) { /* under the pause limit */ } }
      return { verified: (await ctx.page.locator('#out').textContent()) === 'found abcdefghij' };
    },
  });
  assert.notEqual(late.status, 'verified', `verified in ${late.counts?.machine_seconds} s`);
});

test('plant T2g (round 5): run() returns while a save is still under way and verify() reads the back end -> the save is on the clock', async () => {
  // The save answers after 1.5 s (the stand-in's slow POST); verify() reads the back end, not the screen.
  const r = await runDriver({
    async setup(ctx) { ctx.state.before = (await (await fetch(base + '/api/things')).json()).count; },
    async signIn(ctx) { await ctx.page.goto(base + '/save-page'); },
    async run(op) { await op.click('#save'); return {}; },
    async verify(ctx) { return { verified: (await (await fetch(base + '/api/things')).json()).count > ctx.state.before }; },
  });
  if (r.status === 'verified') assert.ok(r.counts.machine_seconds >= 1.5, `verified in ${r.counts.machine_seconds} s, under the save's 1.5 s`);
  assert.ok(r.waits.some(w => w.settle && w.seconds >= 1.4), `the save the driver did not wait for is system wait: ${JSON.stringify(r.waits)}`);
});

test('control (round 5): images and reads still loading when run() returns are not waited for', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/images-page'); },
    async run(op) { await op.click('#q'); return {}; },
    async verify(ctx, outcome) { return { verified: outcome !== undefined }; },
  });
  assert.equal(r.status, 'verified', r.error);
  assert.ok(!r.waits.some(w => w.settle), `a slow image or read was put on the clock: ${JSON.stringify(r.waits)}`);
  assert.ok(r.counts.machine_seconds < 1.5, `machine ${r.counts.machine_seconds}`);
});

test('plant T2c (round 5): a save committed after its answer, and verify() polls the back end until it shows -> invalid', async () => {
  resetAsyncSave();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/async-page'); },
    async run(op) { await op.click('#save'); await op.waitFor(() => document.getElementById('out').textContent === 'accepted'); return {}; },
    async verify() {
      // No timer, no page wait: a loop of reads until the commit shows.
      let saved = false;
      for (let i = 0; i < 5000 && !saved; i++) saved = (await (await fetch(base + '/api/async-saved')).json()).saved;
      return { verified: saved };
    },
  });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error} ${JSON.stringify(r.verify_passes)}`);
  assert.match(r.error, /twice|polled/);
});

test('plant T2d (round 5): a request the page sends 600 ms after the click cannot finish after the clock stops', async () => {
  const hitsBefore = echoHits;
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/debounce-page'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); return {}; },
    async verify(ctx) {
      // A busy wait (no timer, no page wait) before one read.
      const t = Date.now(); while (Date.now() - t < 1500) { /* spin */ }
      return { verified: (await ctx.page.locator('#out').textContent()) === 'found abcdefghij' };
    },
  });
  assert.notEqual(r.status, 'verified', `verified with machine ${r.counts?.machine_seconds}`);
  assert.equal(echoHits, hitsBefore, 'the page\'s request after the clock reached the product');
  assert.equal(r.status, 'invalid', 'the busy wait in verify() is caught too');
  assert.match(r.error, /paused/);
});

test('plant T2h (round 6): verify() that polls is refused on arrival, before the clock as well as after it -> invalid, quickly', async () => {
  resetAsyncSave();
  const t = performance.now();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/async-page'); },
    async run(op) { await op.click('#save'); await op.waitFor(() => document.getElementById('out').textContent === 'accepted'); return {}; },
    async verify() {
      // Polls whether or not the task ran (no outcome check): already before the clock.
      let saved = false;
      for (let i = 0; i < 100000 && !saved; i++) saved = (await (await fetch(`${base}/api/async-saved?n=${i}`)).json()).saved;
      return { verified: saved };
    },
  });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.match(r.error, /verify\(\) before the clock sent over 100 requests: it polled/);
  assert.ok((performance.now() - t) / 1000 < 60, 'refused on arrival, not after the hook timed out');
});

test('plant T2f (round 5): verify() that sleeps on a timer, or polls a slow read with varied requests -> invalid', async () => {
  resetAsyncSave();
  const sleeper = await runDriver({
    signIn: slowSignIn,
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); return {}; },
    async verify(ctx, outcome) { if (outcome !== undefined) await new Promise(res => setTimeout(res, 1500)); return { verified: outcome !== undefined }; },
  });
  assert.equal(sleeper.status, 'invalid', `${sleeper.status} ${sleeper.error}`);
  assert.match(sleeper.error, /paused/);
  resetAsyncSave();
  const poller = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/async-page'); },
    async run(op) { await op.click('#save'); await op.waitFor(() => document.getElementById('out').textContent === 'accepted'); return {}; },
    async verify() {
      let saved = false;
      for (let i = 0; i < 5000 && !saved; i++) saved = (await (await fetch(`${base}/api/async-saved?n=${i}`)).json()).saved;
      return { verified: saved };
    },
  });
  assert.equal(poller.status, 'invalid', `${poller.status} ${poller.error}`);
  assert.match(poller.error, /polled|twice/);
});

test('plant T2e (round 5): verify() that waits with ctx.until or waitForURL -> invalid', async () => {
  for (const wait of [ctx => ctx.until(() => document.getElementById('out').textContent.startsWith('found')),
    ctx => ctx.page.waitForURL('**/plant'), ctx => ctx.page.waitForTimeout(10)]) {
    const r = await runDriver({ ...planted(async () => {}), async verify(ctx, outcome) { if (outcome !== undefined) await wait(ctx); return { verified: true }; } }, TASK, { wait, act: async () => {} });
    assert.equal(r.status, 'invalid', `${wait}: ${r.status} ${r.error}`);
  }
});

test('plant U4 (round 5): a fetch captured when the driver module loads, used while measured -> invalid', async () => {
  const before = things;
  const spec = await sandboxedSource(`
    const fetchAtLoad = globalThis.fetch;
    export default {
      async signIn(ctx) { await ctx.page.goto(${JSON.stringify(base)} + '/plant'); },
      async run(op) { await op.click('#q'); await fetchAtLoad(${JSON.stringify(base)} + '/api/things', { method: 'POST' }); return {}; },
      async verify() { return { verified: true }; },
    };`);
  const r = await execute(TASK, spec, standIn(), 'ours', {}, layout(path.join(tmp, 'u4')), { timeout: 10_000 });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.equal(things, before, 'the back-end write went through');
});

test('plant U5 (round 5): process.getBuiltinModule reaches child_process or net while measured -> invalid, nothing sent', async () => {
  const port = new URL(base).port;
  const acts = {
    'child_process (curl)': async () => {
      const cp = process.getBuiltinModule('node:child_process');
      await new Promise((resolve, reject) => cp.execFile('curl', ['-s', '-X', 'POST', base + '/api/things'], e => (e ? reject(e) : resolve())));
    },
    'net (a raw HTTP request)': async () => {
      const net = process.getBuiltinModule('node:net');
      await new Promise((resolve, reject) => {
        const sock = net.connect(Number(port), '127.0.0.1', () => sock.end('POST /api/things HTTP/1.1\r\nHost: x\r\nContent-Length: 0\r\n\r\n'));
        sock.on('close', resolve); sock.on('error', reject);
      });
    },
    'http (imported at load)': async () => {
      await new Promise((resolve, reject) => { const rq = http.request(base + '/api/things', { method: 'POST' }, resolve); rq.on('error', reject); rq.end(); });
    },
  };
  for (const [name, act] of Object.entries(acts)) {
    const before = things;
    const r = await runDriver(planted(async () => { try { await escape(); } catch { /* swallowed */ } }), TASK, { escape: act, port });
    assert.equal(r.status, 'invalid', `${name}: ${r.status} ${r.error}`);
    assert.match(r.error, /uncounted action/);
    assert.equal(things, before, `${name}: the back-end write went through`);
  }
});

test('plant U5b (round 5): every other way out of the driver process is refused, in set-up too -> invalid', async () => {
  const acts = {
    'a socket opened in set-up to use later': () => { globalThis.kept = process.getBuiltinModule('node:net').connect(Number(new URL(base).port), '127.0.0.1'); },
    'a worker thread': () => new (process.getBuiltinModule('node:worker_threads').Worker)('1', { eval: true }),
    'an inspector session': () => new (process.getBuiltinModule('node:inspector').Session)().connect(),
    'module loader hooks': () => process.getBuiltinModule('node:module').register('data:text/javascript,export {}'),
    'a native binding': () => process.binding('tcp_wrap'),
    'V8 flags': () => process.getBuiltinModule('node:v8').setFlagsFromString('--allow-natives-syntax'),
    'a WebSocket': () => new WebSocket(base.replace('http', 'ws')),
    'a UDP socket': () => process.getBuiltinModule('node:dgram').createSocket('udp4').send('x', 9, '127.0.0.1'),
    'a fetch to another address': () => fetch('http://127.0.0.1:9/api/things', { method: 'POST' }),
  };
  for (const [name, act] of Object.entries(acts)) {
    const r = await runDriver({ ...planted(async () => {}), async setup() { try { await escape(); } catch { /* swallowed */ } } }, TASK, { escape: act });
    assert.equal(r.status, 'invalid', `${name}: ${r.status} ${r.error}`);
  }
});

test('plant U5c (round 5): the driver process cannot write outside its scratch folder, itself or through the harness', async () => {
  const target = path.join(HARNESS_DIR, 'lib', `zz-planted-${process.pid}.mjs`);
  try {
    const direct = await runDriver({ ...planted(async () => {}), async setup() { try { fs.writeFileSync(target, 'export default 1;'); } catch { /* refused */ } } }, TASK, { target });
    assert.equal(fs.existsSync(target), false, 'the driver process wrote into the harness');
    assert.equal(direct.status, 'verified', 'a refused write changes nothing else');
    const viaHarness = await runDriver({ ...planted(async () => {}), async setup(ctx) { await ctx.page.goto(base + '/plant'); try { await ctx.page.screenshot({ path: target }); } catch { /* refused */ } } }, TASK, { target });
    assert.equal(fs.existsSync(target), false, 'the harness wrote a file where the driver asked');
    assert.equal(viaHarness.status, 'invalid', `${viaHarness.status} ${viaHarness.error}`);
    assert.match(viaHarness.error, /outside the driver's scratch folder/);
  } finally { fs.rmSync(target, { force: true }); }
});

test('plant U5d (round 5): a timer left by set-up that calls the back end while measured -> invalid', async () => {
  const before = things;
  const r = await runDriver({ ...planted(async () => { await new Promise(res => setTimeout(res, 3000)); }),
    async signIn(ctx) { setTimeout(() => { fetch(base + '/api/things', { method: 'POST' }).catch(() => {}); }, 1200); await ctx.page.goto(base + '/plant'); } });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.equal(things, before);
});

test('plant U5h (round 5): a slow back-end call set-up leaves running lands before the clock, never inside it -> invalid', async () => {
  const r = await runDriver({
    async setup(ctx) {
      ctx.state.before = (await (await fetch(base + '/api/things')).json()).count;
      fetch(base + '/api/slow-things', { method: 'POST' }).catch(() => {}); // not awaited: it would finish while measured
    },
    async signIn(ctx) { await ctx.page.goto(base + '/plant'); },
    async run(op) { await op.click('#q'); await new Promise(res => setTimeout(res, 2500)); return {}; },
    async verify(ctx) { return { verified: (await (await fetch(base + '/api/things')).json()).count > ctx.state.before }; },
  });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.match(r.error, /already done before the clock/);
});

test('plant U5i (round 5): page script smuggled as a crafted function text cannot run outside the sentinel -> invalid', async () => {
  // The driver process sends functions as their text; a crafted text would close the sentinel's
  // call and click Go outside it. Sent as a plain object, and through a patched toString.
  for (const via of ['object', 'toString']) {
    const r = await runDriver(planted(async op => {
      const crafted = "() => true), document.getElementById('go').click(), (() => true";
      if (via === 'object') await op.waitFor({ __fn: crafted });
      else { const f = () => true; Object.defineProperty(f, 'toString', { value: () => crafted }); Function.prototype.toString = function () { return crafted; }; await op.waitFor(f); }
    }), TASK, { via });
    assert.equal(r.status, 'invalid', `${via}: ${r.status} ${r.error}`);
    assert.match(r.error, /not exactly one function expression/);
  }
});

test('plant U5j (round 5): set-up leaves a request of Playwright\'s own HTTP client running -> invalid', async () => {
  const r = await runDriver({ ...planted(async () => {}), async setup(ctx) { await ctx.page.goto(base + '/plant'); try { ctx.page.request.post(base + '/api/slow-things').catch(() => {}); } catch { /* refused */ } } });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.match(r.error, /APIRequestContext/);
});

test('plant U5k (round 5): an API session with a header that changes what a typed request does -> invalid', async () => {
  const task = { id: 'api-plant', title: 'API plant', channel: 'api', input: {} };
  const r = await runDriver({
    async signIn(ctx) { await ctx.useApi({ baseUrl: base, headers: { Authorization: 'Bearer t', 'X-HTTP-Method-Override': 'POST' } }); },
    async run(op) { await op.request('GET', '/api/things'); return {}; },
    async verify(ctx, outcome) { return { verified: outcome !== undefined }; },
  }, task);
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.match(r.error, /header/);
});

test('plant U5e (round 5): no driver module runs in the harness process, and its clock is out of the driver\'s reach', async () => {
  delete globalThis.__plantTopLevel;
  const spec = await sandboxedSource(`
    globalThis.__plantTopLevel = true;
    Performance.prototype.now = () => 0;
    performance.now = () => 0;
    Date.now = () => 0;
    export default {
      async signIn(ctx) { await ctx.page.goto(${JSON.stringify(base)} + '/slow-page'); },
      async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'found abcdefghij'); return {}; },
      async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'found abcdefghij' }; },
    };`);
  const r = await execute(TASK, spec, standIn(), 'ours', {}, layout(path.join(tmp, 'u5e')), { timeout: 10_000 });
  assert.equal(globalThis.__plantTopLevel, undefined, 'the driver module ran in the harness process');
  assert.equal(r.status, 'verified', r.error);
  assert.ok(r.counts.machine_seconds >= 2, `machine ${r.counts.machine_seconds}: the driver moved the clock`);
});

test('plant U5f (round 5): a driver object handed straight to execute() is refused, never measured', async () => {
  const r = await execute(TASK, { async signIn(ctx) { await ctx.page.goto(base + '/plant'); }, run: honestRun, verify: verifyFound }, standIn(), 'ours', {}, layout(path.join(tmp, 'u5f')), { timeout: 10_000 });
  assert.equal(r.status, 'invalid');
  assert.match(r.error, /driver process/);
});

test('plant U5g (round 5, the real driver): ours api-update-user sends its PUT through a child process -> invalid, nothing sent', async () => {
  // The critic's plant (gauntlet/evidence/p01-odoo-rig/r5/plants/plant-U5-api-update-user-child-process.diff), on the driver as it stands.
  const file = plantedFile(path.join(HARNESS_DIR, 'drivers', 'ours', 'api-update-user.mjs'), s0 => {
    const s1 = s0.replace("ctx.useApi({ baseUrl: ctx.product.baseUrl, headers: { Authorization: `Bearer ${api.token}`, 'X-Erp-Request': '1' } });",
      "ctx.useApi({ baseUrl: ctx.product.baseUrl, headers: { Authorization: `Bearer ${api.token}`, 'X-Erp-Request': '1' } });\n    ctx.state.token = api.token;");
    const planted = s1.replace(/\n    await op\.request\('PUT'[^\n]*\n/, `
    const cp = process.getBuiltinModule('node:child_process');
    const body = JSON.stringify({ displayName: u.displayName, language: 'ar', isActive: u.isActive, roleIds: u.roleIds, version: u.version });
    await new Promise((resolve, reject) => cp.execFile('curl', ['-sf', '-X', 'PUT', '-H', 'Content-Type: application/json', '-H', 'X-Erp-Request: 1',
      '-H', \`Authorization: Bearer \${ctx.state.token}\`, '--data', body, \`\${ctx.product.baseUrl}/api/identity/users/\${u.id}\`], e => (e ? reject(e) : resolve())));
`);
    assert.notEqual(planted, s1, 'the plant must change the driver');
    return planted;
  });
  const honest = await execute({ id: 'api-update-user', title: 'API', channel: 'api', startAt: 'api', moments: [], input: {} }, await describeDriverFile(path.join(HARNESS_DIR, 'drivers', 'ours', 'api-update-user.mjs')),
    { id: 'ours', baseUrl: base, users: { admin: { login: 'api.tester', password: 'b' } }, brandWords: [] }, 'ours', needles, layout(path.join(tmp, 'u5g-honest')), { timeout: 10_000 });
  assert.equal(honest.status, 'verified', `the honest driver on the stand-in: ${honest.error}`);
  assert.equal(honest.counts.steps, 2);
  const r = await execute({ id: 'api-update-user', title: 'API', channel: 'api', startAt: 'api', moments: [], input: {} }, await describeDriverFile(file),
    { id: 'ours', baseUrl: base, users: { admin: { login: 'api.tester', password: 'b' } }, brandWords: [] }, 'ours', needles, layout(path.join(tmp, 'u5g')), { timeout: 10_000 });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error} steps ${r.counts?.steps}`);
  assert.equal(curlRequests, 0, 'the uncounted PUT reached the back end');
});

for (const [name, chord, copyFirst] of [['C2 (round 5): ControlOrMeta+v', 'ControlOrMeta+v', false], ['C3 (round 5): an empty copy, then Control+v', 'Control+v', true],
  ['C4 (round 5): Control+KeyV', 'Control+KeyV', false], ['C5 (round 5): control+V written in other cases', 'control+V', false], ['C6 (round 5): Shift+Control+v', 'Shift+Control+v', false]]) {
  test(`plant ${name}: text copied in set-up is pasted while measured -> invalid`, async () => {
    const r = await runDriver({
      async signIn(ctx) {
        await ctx.page.goto(base + '/slow-page');
        await ctx.page.locator('#q').fill('abcdefghij');
        await ctx.page.locator('#q').press('Control+a');
        await ctx.page.locator('#q').press('Control+c');
        await ctx.page.locator('#q').fill('');
      },
      async run(op) {
        await op.click('#q');
        if (copyFirst) await op.press('Control+c'); // nothing selected in the empty field
        await op.press(chord); await op.click('#go');
        await op.waitFor(() => document.getElementById('out').textContent === 'found abcdefghij', { timeout: 5000 });
        return {};
      },
      async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'found abcdefghij' }; },
    }, TASK, { chord, copyFirst });
    assert.equal(r.status, 'invalid', `${chord}: ${r.status} ${r.error}`);
    assert.match(r.error, /not copied inside the measured part/);
  });
}

test('control (round 5): the clipboard is empty at the start, whatever set-up copied', async () => {
  const r = await runDriver({
    async signIn(ctx) {
      await ctx.page.goto(base + '/plant');
      await ctx.page.locator('#q').fill('abcdefghij');
      await ctx.page.locator('#q').press('Control+a');
      await ctx.page.locator('#q').press('Control+c');
      await ctx.page.locator('#q').fill('');
    },
    async run(op, ctx) {
      // A measured copy of one character, then two pastes: the clipboard holds that character only.
      await op.fill('#q', 'z'); await op.press('Control+a'); await op.press('Control+c'); await op.press('End'); await op.press('Control+v');
      assert.equal(await op.page.locator('#q').inputValue(), 'zz');
      return {};
    },
    async verify(ctx) { return { verified: (await ctx.page.locator('#q').inputValue()) === 'zz' }; },
  });
  assert.equal(r.status, 'verified', `${r.status} ${r.error}`);
  const copy = r.steps.find(st => st.chord === 'Control+c');
  assert.equal(copy.copied_chars, 1);
});

test('KLM (round 5): typing right after an Enter that opened a new screen starts with a mental step', async () => {
  const task = { ...TASK, id: 'plant-nav' };
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/nav-form'); },
    async run(op) {
      await op.click('#go'); await op.type('list'); await op.press('Enter');
      await op.waitFor('#s:focus', { label: 'the list opened' });
      await op.type('abc');
      await op.waitFor(() => document.getElementById('out')?.textContent === 'found abc');
      return {};
    },
    async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'found abc' }; },
  }, task);
  assert.equal(r.status, 'verified', r.error);
  const [, typed, enter, typedAfter] = r.steps;
  assert.equal(typed.chain, true, 'typing after the click into the field continues it');
  assert.equal(enter.chain, true, 'Enter after typing continues it');
  assert.equal(enter.screen, '/nav-form');
  assert.equal(typedAfter.screen, '/nav-list');
  assert.equal(typedAfter.chain, false, 'typing on the new screen starts with M');
  assert.equal(r.counts.klm_operator_counts.M, 2);
});
