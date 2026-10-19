// Round 14 (critic p01 r13, biggest gap): every rule that tied the end state to what the person did
// applied only to a task that saves. Eleven tasks do not save (find-record among them); for those, set-up
// could leave the task done, or half done, for the product to finish, and the critic's plants N1, N1b, N2
// and N3 were recorded as verified wins. Now, for every browser task:
//
//   - the browser's record of the page runs from before verify()'s read on the start screen until the
//     page's script is frozen after the clock, and every change of what the page shows must lead back to
//     a counted step (lib/causation.mjs screenCauses; enforced for the product measured against the
//     reference); the clock starts 1.1-2.1 s after verify()'s read for every browser task;
//   - a task that names the record the person finds (`finds`) is refused when the measured part typed no
//     piece of it, or when the screen showed it before the first piece was typed, or the start screen
//     showed it (lib/runner.mjs findsProblem);
//   - set-up's writes may not carry the record the person finds, and set-up's browser may not open an
//     address that names the task's data (lib/runner.mjs carriesFound, addressNames).
//
// Plants (each must never be recorded as verified), on a stand-in product with a real browser:
//   N1   set-up turns on "open the last record at start": the start page opens the record on its own
//        timer, before the clock; run() presses Escape.
//   N1L  the same timer firing inside the measured part (4 s); run() presses Escape and waits.
//   N1b  the same, run() clicks plain text once.
//   N1c  the start page's timer shows the record in place (no navigation, no address change).
//   N1d  the start page polls the back end; set-up told the back end to answer "open" a while into the clock.
//   N2   set-up opened the record (its address names it); the start screen offers it as a link.
//   N2b  set-up opened the record by its id; the start screen offers it as a link.
//   N2c  the link shows on the list, a click away from the start: click Contacts, click the link.
//   N2d  N2c with two letters of the name typed into the list's search first.
//   N3   set-up saves a default view of the list whose search is the record's name (back-end call);
//        run(): click Contacts, Enter.
//   N3b  the same view saved by set-up's browser.
//   N3c  the default view searches part of the name ("Shamma Waleed"); run() types two letters.
// Controls (verify): the honest search; the same behind a 0.3 s debounce and a 2.5 s answer; the initials
// as the typed piece; a page that opens the record 1.5 s after the person's click.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout, findsProblem, isPieceOf, carriesFound, foundWords, addressNames, screenProblem } from '../lib/runner.mjs';
import { loadNeedles } from '../data/generate.mjs';
import { sandboxed } from './helpers/driver-module.mjs';

let server, base, tmp, needles;
const stamps = [];
const state = { openAtStart: 0, inPlace: false, poll: false, goAt: 0, recent: null, view: null, slow: 0, onShot: false, onRead: false };
const reset = () => Object.assign(state, { openAtStart: 0, inPlace: false, poll: false, goAt: 0, recent: null, view: null, slow: 0, onShot: false, onRead: false });
const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

const RECORD_HTML = n => `<aside id="record"><h1>${esc(n.name)}</h1><dl><dt>Mobile</dt><dd>${esc(n.mobile)}</dd></dl></aside>`;
const recentLink = () => (state.recent ? `<p>Recently viewed: <a id="recent" href="/contact/r1">${esc(state.recent.name)}</a></p>` : '');

const HOME = () => `<!doctype html><html><head><title>Home</title></head><body>
<nav aria-label="Main navigation"><a id="contacts" href="/contacts">Contacts</a></nav>
<p id="plain">Welcome</p>
<label id="field">Note <input id="note"></label>
<a id="stamp" href="/stamped">Stamp</a>
${recentLink()}
<div id="slot"></div>
<script>
${state.openAtStart ? `setTimeout(() => { ${state.inPlace ? `document.getElementById('slot').innerHTML = ${JSON.stringify(RECORD_HTML(needles.contact))};` : "location.href = '/contact/r1';"} }, ${state.openAtStart});` : ''}
${state.onShot ? `new MutationObserver(() => { if (document.title === 'Product') document.getElementById('slot').innerHTML = ${JSON.stringify(RECORD_HTML(needles.contact))}; }).observe(document.querySelector('title'), { childList: true, characterData: true, subtree: true });` : ''}
${state.onRead ? `const own = Node.prototype.contains; Node.prototype.contains = function (x) { document.getElementById('slot').innerHTML = ${JSON.stringify(RECORD_HTML(needles.contact))}; return own.call(this, x); };` : ''}
${state.poll ? "const t = setInterval(async () => { const r = await fetch('/api/go'); if ((await r.text()) === 'go') { clearInterval(t); location.href = '/contact/r1'; } }, 200);" : ''}
</script></body></html>`;

const LIST = () => `<!doctype html><html><head><title>Contacts</title></head><body>
<nav aria-label="Main navigation"><a href="/">Home</a></nav>
<input type="search" id="q" aria-label="Search" autofocus value="${esc(state.view?.search || '')}">
${recentLink()}
<div id="rows">${state.view?.search && needles.contact.name.toLowerCase().includes(state.view.search.toLowerCase())
    ? `<div class="row"><a href="/contact/r1">${esc(needles.contact.name)}</a></div>`
    : Array.from({ length: 20 }, (_, i) => `<div class="row">Contact ${i + 1}</div>`).join('')}</div>
<script>
const q = document.getElementById('q');
let debounce = null;
q.addEventListener('keydown', e => {
  if (e.key !== 'Enter' || !q.value.trim()) return;
  ${state.slow ? "clearTimeout(debounce); debounce = setTimeout(async () => { const r = await fetch('/api/search?q=' + encodeURIComponent(q.value.trim())); location.href = await r.text(); }, 300);" : "location.href = '/contact?n=' + encodeURIComponent(q.value.trim());"}
});
</script></body></html>`;

const RECORD = n => `<!doctype html><html><head><title>Contact</title></head><body><nav aria-label="Main navigation"><a href="/contacts">Contacts</a></nav>${RECORD_HTML(n)}</body></html>`;

const matches = q => {
  const t = q.toLowerCase().replace(/[^a-z]/g, '');
  const name = needles.contact.name.toLowerCase();
  if (name.replace(/[^a-z]/g, '') === t || name.includes(q.toLowerCase())) return true;
  return name.split(/\s+/).map(w => w[0]).join('') === t; // the initials
};

before(async () => {
  needles = loadNeedles();
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    const html = body => { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(body); };
    let body = '';
    req.on('data', c => { body += c; });
    req.on('end', () => {
      if (u.pathname === '/api/settings' && req.method === 'POST') { Object.assign(state, JSON.parse(body || '{}')); res.writeHead(204); return res.end(); }
      if (u.pathname === '/api/views' && req.method === 'POST') { state.view = JSON.parse(body || '{}'); res.writeHead(201); return res.end('{}'); }
      if (u.pathname === '/api/views-form' && req.method === 'POST') { state.view = { search: new URLSearchParams(body).get('search') }; return html('<!doctype html><p id="saved">Saved</p>'); }
      if (u.pathname === '/views/new') return html('<!doctype html><form method="post" action="/api/views-form"><input name="search" aria-label="Search"><button>Save</button></form>');
      if (u.pathname === '/api/stamp') { stamps.push({ what: u.searchParams.get('what'), at: Date.now() }); res.writeHead(200); return res.end('{}'); }
      if (u.pathname === '/stamped') { stamps.push({ what: 'step', at: Date.now() }); return html('<!doctype html><p id="stamped">ok</p>'); }
      if (u.pathname === '/api/go') { res.writeHead(200); return res.end(state.goAt && Date.now() >= state.goAt ? 'go' : 'wait'); }
      if (u.pathname === '/api/search') {
        const q = u.searchParams.get('q') || '';
        return setTimeout(() => { res.writeHead(200); res.end(matches(q) ? '/contact/r1' : '/contacts'); }, state.slow);
      }
      if (u.pathname === '/contact' || u.pathname.startsWith('/contact/')) {
        const n = u.searchParams.get('n');
        if (n !== null && !matches(n)) return html('<!doctype html><p>No match</p>');
        state.recent = needles.contact; // the server remembers the record as recently viewed
        return html(RECORD(needles.contact));
      }
      if (u.pathname === '/contacts') return html(LIST());
      return html(HOME());
    });
  });
  server.keepAliveTimeout = 1;
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-screen-causes-'));
});
after(() => {
  server?.close(); server?.closeAllConnections?.();
  fs.rmSync(tmp, { recursive: true, force: true });
});

// A find-record-shaped task: it does not save; the person opens one contact among many.
const TASK = { id: 'plant-find', title: 'Plant (find one record)', startAt: 'home', moments: [], input: {}, data: ['contact.name', 'contact.mobile'], finds: 'contact.name' };
const standIn = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'admin', password: 'x' } }, brandWords: [], readyKind: 'default' });
const out = () => layout(path.join(tmp, String(Math.random()).slice(2)));
const runDriver = async (driver, task = TASK) => execute(task, await sandboxed(driver, { base }), standIn(), 'ours', needles, out(), { timeout: 12_000 });
const verifyRecord = async ctx => {
  const { name, mobile } = ctx.needles.contact;
  const shown = await ctx.read(([n, m]) => { const a = document.querySelector('aside'); return !!a && a.innerText.includes(n) && a.innerText.includes(m); }, [name, mobile]);
  return { verified: shown };
};
const home = { async signIn(ctx) { await ctx.page.goto(base + '/'); } };
const refused = (name, r, why) => {
  assert.notEqual(r.status, 'verified', `MISSED (${name}): verified with ${r.counts?.steps} steps and ${r.counts?.keystrokes} keys`);
  assert.equal(r.status, 'invalid', `${name}: ${r.status} ${r.error}`);
  if (why) assert.match(r.error, why, `${name}: refused for another reason: ${r.error}`);
};
const SCREEN = /changed what it shows without a counted step/;
const FINDS = /typed no piece|already showed|start screen already shows/;

// -- the rules alone ------------------------------------------------------------------------------

test('a typed piece of a name: its letters in order, at least two', () => {
  const name = 'Shamma Waleed Al Romaithi';
  for (const t of ['Shamma Waleed Al Romaithi', 'shamma', 'swar', 'sh wa', 'Rom', 'al rom', 'SHAMMA-WALEED']) assert.equal(isPieceOf(t, name), true, t);
  for (const t of ['s', '', 'users', 'xyz', 'romaithi shamma', 'Shamma Waleed Al Romaithi X']) assert.equal(isPieceOf(t, name), false, t);
});

test('findsProblem: nothing typed, shown before, unreadable, and the health check', () => {
  assert.match(findsProblem('A B', { typed_at_step: null, shown_before: null }), /typed no piece/);
  assert.match(findsProblem('A B', { typed_at_step: 2, shown_before: true }), /already showed/);
  assert.match(findsProblem('A B', { typed_at_step: 2, shown_before: null, read_error: 'x' }), /could not be read/);
  assert.equal(findsProblem('A B', { typed_at_step: 2, shown_before: false }), null);
  assert.equal(findsProblem('A B', { typed_at_step: 2, shown_before: true }, { enforceShown: false }), null);
  assert.match(findsProblem('A B', { typed_at_step: null, shown_before: null }, { enforceShown: false }), /typed no piece/);
  assert.equal(findsProblem(null, null), null);
});

test('carriesFound: the name or a word of it in a set-up write, the task\'s own inputs taken out', () => {
  const task = { finds: 'contact.name', input: { newEmail: 'shamma.romaithi.new@mail.example' } };
  const g = foundWords(task, 'ours', { contact: { name: 'Shamma Waleed Al Romaithi' } });
  assert.equal(carriesFound('POST /api/lists/views {"search":"Shamma Waleed Al Romaithi","isDefault":true}', g), true);
  assert.equal(carriesFound('{"search":"shamma waleed"}', g), true);
  assert.equal(carriesFound('{"search":"Romaithi"}', g), true);
  assert.equal(carriesFound('{"search":"\\u0053hamma"}', g), true);
  assert.equal(carriesFound('/api/views?search=Shamma%20Waleed', g), true);
  assert.equal(carriesFound('{"email":"shamma.romaithi.new@mail.example"}', g), false);
  assert.equal(carriesFound('{"language":"ar","numerals":"latn"}', g), false);
  assert.equal(foundWords({ input: {} }, 'ours', {}), null);
});

test('addressNames: a set-up address that names the task\'s data', () => {
  assert.equal(addressNames('http://p/contact?n=Shamma%20Waleed%20Al%20Romaithi', ['shamma waleed al romaithi']), true);
  assert.equal(addressNames('http://p/contact/C073519', ['shamma waleed al romaithi']), false);
  assert.equal(addressNames('http://p/', ['shamma waleed al romaithi']), false);
});

test('screenProblem: enforced for ours, recorded for the reference', () => {
  const u = [{ at: -0.4, event: 'ParseHTML', why: 'a timer armed before the clock started' }];
  assert.match(screenProblem('ours', u), /without a counted step/);
  assert.equal(screenProblem('odoo', u), null);
  assert.equal(screenProblem('ours', []), null);
});

// -- controls ------------------------------------------------------------------------------------

const honest = {
  ...home,
  async run(op, ctx) {
    await op.click('#contacts', { label: 'Contacts' });
    await op.waitFor(() => document.activeElement && document.activeElement.id === 'q', { label: 'list' });
    await op.type(ctx.needles.contact.name, { label: 'name' });
    await op.press('Enter', { label: 'open' });
    await op.waitFor(() => !!document.querySelector('aside'), { label: 'record', timeout: 10_000 });
    return {};
  },
  verify: verifyRecord,
};

test('control: the honest search (Contacts, type the name, Enter) verifies, its screen changes all caused', async () => {
  reset();
  const r = await runDriver(honest);
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.screen_changes.not_caused, 0, JSON.stringify(r.screen_changes));
  assert.ok(r.screen_changes.judged > 0);
  assert.equal(r.finds.shown_before, false);
  assert.equal(r.finds.typed_at_step, 2);
});

test('control: the search behind a 0.3 s debounce and a 2.5 s answer verifies (a slow product is still the person\'s doing)', async () => {
  reset();
  state.slow = 2500;
  const r = await runDriver(honest);
  assert.equal(r.status, 'verified', r.error);
});

test('control: the initials as the typed piece verify', async () => {
  reset();
  const r = await runDriver({
    ...honest,
    async run(op, ctx) {
      await op.click('#contacts', { label: 'Contacts' });
      await op.waitFor(() => document.activeElement && document.activeElement.id === 'q', { label: 'list' });
      await op.type(ctx.needles.contact.name.split(/\s+/).map(w => w[0]).join('').toLowerCase(), { label: 'initials' });
      await op.press('Enter', { label: 'open' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record' });
      return {};
    },
  });
  assert.equal(r.status, 'verified', r.error);
});

// -- plants: the product finishes the task on its own (N1) -------------------------------------------

test('plant N1: the start page opens the record on its own timer before the clock; run() presses Escape -> refused', async () => {
  reset();
  refused('N1', await runDriver({
    async setup() { await fetch(base + '/api/settings', { method: 'POST', body: JSON.stringify({ openAtStart: 1600 }) }); },
    ...home,
    async run(op) {
      await op.press('Escape', { label: 'dismiss' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record', timeout: 9000 });
      return {};
    },
    verify: verifyRecord,
  }), SCREEN);
});

test('plant N1L: the start page opens the record on its own timer inside the measured part; run() presses Escape -> refused', async () => {
  reset();
  refused('N1L', await runDriver({
    async setup() { await fetch(base + '/api/settings', { method: 'POST', body: JSON.stringify({ openAtStart: 4500 }) }); },
    ...home,
    async run(op) {
      await op.press('Escape', { label: 'dismiss' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record', timeout: 9000 });
      return {};
    },
    verify: verifyRecord,
  }), SCREEN);
});

test('plant N1b: the same timer; run() clicks plain text once -> refused', async () => {
  reset();
  refused('N1b', await runDriver({
    async setup() { await fetch(base + '/api/settings', { method: 'POST', body: JSON.stringify({ openAtStart: 4500 }) }); },
    ...home,
    async run(op) {
      await op.click('#plain', { label: 'plain text' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record', timeout: 9000 });
      return {};
    },
    verify: verifyRecord,
  }), SCREEN);
});

test('plant N1c: the start page\'s timer shows the record in place (no navigation, no address change) -> refused', async () => {
  reset();
  refused('N1c', await runDriver({
    async setup() { await fetch(base + '/api/settings', { method: 'POST', body: JSON.stringify({ openAtStart: 4500, inPlace: true }) }); },
    ...home,
    async run(op) {
      await op.click('#plain', { label: 'plain text' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record', timeout: 9000 });
      return {};
    },
    verify: verifyRecord,
  }), SCREEN);
});

test('plant N1d: the start page polls; set-up told the back end to answer "open" inside the measured part -> refused', async () => {
  reset();
  refused('N1d', await runDriver({
    async setup() { await fetch(base + '/api/settings', { method: 'POST', body: JSON.stringify({ poll: true, goAt: Date.now() + 9000 }) }); },
    ...home,
    async run(op) {
      await op.click('#plain', { label: 'plain text' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record', timeout: 11_000 });
      return {};
    },
    verify: verifyRecord,
  }), SCREEN);
});

// -- plants: set-up put the record within reach (N2, N3) ---------------------------------------------

const clickRecent = {
  async run(op) {
    await op.click('#recent', { label: 'recently viewed' });
    await op.waitFor(() => !!document.querySelector('aside'), { label: 'record' });
    return {};
  },
  verify: verifyRecord,
};

test('plant N2: set-up opened the record by an address naming it; the start offers it as a link -> refused', async () => {
  reset();
  refused('N2', await runDriver({
    async signIn(ctx) {
      await ctx.page.goto(base + '/contact?n=' + encodeURIComponent(ctx.needles.contact.name));
      await ctx.page.goto(base + '/');
    },
    ...clickRecent,
  }));
});

test('plant N2b: set-up opened the record by its id; the start screen offers it as a link -> refused (the start shows the record)', async () => {
  reset();
  refused('N2b', await runDriver({
    async signIn(ctx) {
      await ctx.page.goto(base + '/contact/r1');
      await ctx.page.goto(base + '/');
    },
    ...clickRecent,
  }), FINDS);
});

test('plant N2c: the remembered record is offered on the list, a click from the start -> refused (nothing typed)', async () => {
  reset();
  state.recent = needles.contact;
  refused('N2c', await runDriver({
    ...home,
    async run(op) {
      await op.click('#contacts', { label: 'Contacts' });
      await op.click('#recent', { label: 'recently viewed' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record' });
      return {};
    },
    verify: verifyRecord,
  }), FINDS);
});

test('plant N2d: two letters typed, then the remembered record clicked -> refused (on screen before the piece was typed)', async () => {
  reset();
  state.recent = needles.contact;
  refused('N2d', await runDriver({
    ...home,
    async run(op, ctx) {
      await op.click('#contacts', { label: 'Contacts' });
      await op.type(ctx.needles.contact.name.slice(0, 2), { label: 'two letters' });
      await op.click('#recent', { label: 'recently viewed' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record' });
      return {};
    },
    verify: verifyRecord,
  }), FINDS);
});

const enterOnView = {
  ...home,
  async run(op) {
    await op.click('#contacts', { label: 'Contacts' });
    await op.waitFor(() => document.activeElement && document.activeElement.id === 'q', { label: 'list' });
    await op.press('Enter', { label: 'open the best match' });
    await op.waitFor(() => !!document.querySelector('aside'), { label: 'record' });
    return {};
  },
  verify: verifyRecord,
};

test('plant N3: set-up saves a default view searching the record\'s name (back-end call); run(): Contacts, Enter -> refused', async () => {
  reset();
  refused('N3', await runDriver({
    async setup(ctx) { await fetch(base + '/api/views', { method: 'POST', body: JSON.stringify({ name: 'Mine', search: ctx.needles.contact.name, isDefault: true }) }); },
    ...enterOnView,
  }), /carries the record the person finds/);
});

test('plant N3b: the same default view saved by set-up\'s browser (a form it fills and posts) -> refused', async () => {
  reset();
  refused('N3b', await runDriver({
    ...enterOnView,
    async signIn(ctx) {
      await ctx.page.goto(base + '/views/new');
      await ctx.page.locator('input[name="search"]').fill(ctx.needles.contact.name);
      await ctx.page.getByRole('button', { name: 'Save' }).click();
      await ctx.page.locator('#saved').waitFor();
      await ctx.page.goto(base + '/');
    },
  }), /carry the record the person finds/);
});

test('plant N3c: the default view searches part of the name; run() types two letters and Enter -> refused', async () => {
  reset();
  refused('N3c', await runDriver({
    async setup(ctx) { await fetch(base + '/api/views', { method: 'POST', body: JSON.stringify({ name: 'Mine', search: ctx.needles.contact.name.split(' ').slice(0, 2).join(' '), isDefault: true }) }); },
    ...home,
    async run(op, ctx) {
      await op.click('#contacts', { label: 'Contacts' });
      await op.type(ctx.needles.contact.name.slice(0, 2).toLowerCase(), { label: 'two letters' });
      await op.press('Enter', { label: 'open' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record' });
      return {};
    },
    verify: verifyRecord,
  }), /carries the record the person finds/);
});

// -- plants: the page acts inside the harness's own work (N1e, N1f), and set-up's address (N2x) ----

const NO_FINDS = { ...TASK, id: 'plant-nofind', finds: undefined };

test('plant N1e: the page shows the record when the harness neutralises the title for the start shot (an observer of the title) -> refused', async () => {
  reset();
  state.onShot = true;
  refused('N1e', await runDriver({
    ...home,
    async run(op) {
      await op.press('Escape', { label: 'dismiss' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record' });
      return {};
    },
    verify: verifyRecord,
  }, NO_FINDS), SCREEN);
});

test('plant N1f: the page shows the record when the harness reads whether the typing goes into the clicked field (a patched Node#contains) -> refused', async () => {
  reset();
  state.onRead = true;
  refused('N1f', await runDriver({
    ...home,
    async run(op) {
      await op.click('#field', { label: 'note' });
      await op.type('zz', { label: 'two letters' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record' });
      return {};
    },
    verify: verifyRecord,
  }, NO_FINDS), SCREEN);
});

test('plant N2x: the critic\'s N2 shape on a task that names no record to find: set-up opened an address naming the record -> refused', async () => {
  reset();
  refused('N2x', await runDriver({
    async signIn(ctx) {
      await ctx.page.goto(base + '/contact?n=' + encodeURIComponent(ctx.needles.contact.name));
      await ctx.page.goto(base + '/');
    },
    ...clickRecent,
  }, NO_FINDS), /opened an address that names the task's data/);
});

test('control: a task that names no record to find, the page answering only the person (click the note, type) verifies', async () => {
  reset();
  const r = await runDriver({
    ...home,
    async run(op) {
      await op.click('#field', { label: 'note' });
      await op.type('zz', { label: 'two letters' });
      await op.click('#contacts', { label: 'Contacts' });
      await op.waitFor(() => !!document.querySelector('#q'), { label: 'list' });
      return {};
    },
    async verify(ctx) { return { verified: await ctx.read(() => !!document.querySelector('#q')) }; },
  }, NO_FINDS);
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.screen_changes.not_caused, 0, JSON.stringify(r.screen_changes));
});

test('the clock starts 1.1-2.1 s after verify()\'s read before it, for a task that does not save too (critic r13)', async () => {
  reset();
  stamps.length = 0;
  const r = await runDriver({
    ...home,
    async run(op) {
      await op.click('#stamp', { label: 'stamp' });
      await op.waitFor(() => !!document.querySelector('#stamped'), { label: 'stamped' });
      return {};
    },
    async verify(ctx) {
      await fetch(ctx.product.baseUrl + '/api/stamp?what=verify');
      return { verified: await ctx.read(() => !!document.querySelector('#stamped')) };
    },
  }, NO_FINDS);
  assert.equal(r.status, 'verified', r.error);
  const read = stamps.find(x => x.what === 'verify');
  const step = stamps.find(x => x.what === 'step');
  assert.ok(read && step, JSON.stringify(stamps));
  const gap = (step.at - read.at) / 1000;
  assert.ok(gap >= 1.1, `the step came ${gap} s after verify()'s read`);
});
