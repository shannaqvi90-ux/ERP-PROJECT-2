// Critic p01 round 14 plants (fresh critic). Stand-in product, same scaffolding as test/screen-causes.test.mjs.
// Each plant must never be recorded as 'verified'. The stand-in task is find-user's shape and declares
// what find-user declares: `finds` (the needle's name) and `fixtures: { ours: [] }` (set-up writes nothing),
// so every round-14 rule is in force (screen causes, a typed piece, nothing on screen before it, no set-up
// write, no set-up address naming the data).
//
//   P1  set-up's browser opens the record by its id (a GET; the address names no task data). The product
//       remembers it server side and ranks it first in any search it matches. run(): Contacts, type the
//       name's first two letters, Enter. Two letters match a large part of 100,000 names; the record opens
//       only because set-up viewed it.
//   P1b the same memory made by the harness's fetch (a GET of the record by id, no browser).
//   P2  the remembered record is offered on the start screen as a link whose text is drawn by CSS
//       (::after { content: attr(data-name) }): a person sees the whole name; body.innerText does not hold
//       it. run(): type two letters of the name into a note field (does nothing), click the link.
//   P3  set-up (a GET) arms the back end; any click starts the list's poll (a repeating timer armed by the
//       click's document), and the back end answers "open" on its own schedule. run(): Contacts, type two
//       letters (they do nothing), wait. The record opens on the server's timer.
//   C0  control: the honest whole-name search verifies.
//   C1  control: P1's path without set-up's visit does not open the record (two letters find nothing).
//   C3  control: P3's path without set-up's arming does not open the record.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout, compareRuns } from '../lib/runner.mjs';
import { loadNeedles } from '../data/generate.mjs';
import { sandboxed } from './helpers/driver-module.mjs';

let server, base, tmp, needles;
const state = { recent: false, armed: false, polls: 0, cssRecent: false, poll: false };
const reset = () => Object.assign(state, { recent: false, armed: false, polls: 0, cssRecent: false, poll: false });
const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const letters = s => String(s).toLowerCase().replace(/[^a-z]/g, '');
const isPiece = (typed, name) => { const t = letters(typed); let i = 0; for (const ch of letters(name)) if (ch === t[i]) i++; return t.length >= 2 && i === t.length; };

const HOME = () => `<!doctype html><html><head><title>Home</title>
<style>#recent::after { content: attr(data-name); }</style></head><body>
<nav aria-label="Main navigation"><a id="contacts" href="/contacts">Contacts</a></nav>
<p id="plain">Welcome</p>
<label id="field">Note <input id="note"></label>
${state.cssRecent && state.recent ? `<p>Recently viewed: <a id="recent" href="/contact/r1" data-name="${esc(needles.contact.name)}"></a></p>` : ''}
</body></html>`;

const LIST = () => `<!doctype html><html><head><title>Contacts</title></head><body>
<nav aria-label="Main navigation"><a href="/">Home</a></nav>
<input type="search" id="q" aria-label="Search" autofocus>
<div id="rows">${Array.from({ length: 20 }, (_, i) => `<div class="row">Contact ${i + 1}</div>`).join('')}</div>
<script>
const q = document.getElementById('q');
q.addEventListener('keydown', e => { if (e.key === 'Enter' && q.value.trim()) location.href = '/contact?n=' + encodeURIComponent(q.value.trim()); });
// Notifications poll (the product's own, P3 and C3 only); the back end may answer "open".
${state.poll ? '' : '/*'}const t = setInterval(async () => { const r = await fetch('/api/notifications'); if ((await r.text()) === 'open') { clearInterval(t); location.href = '/contact/r1'; } }, 200);${state.poll ? '' : '*/'}
</script></body></html>`;

const RECORD = n => `<!doctype html><html><head><title>Contact</title></head><body><nav aria-label="Main navigation"><a href="/contacts">Contacts</a></nav>
<aside id="record"><h1>${esc(n.name)}</h1><dl><dt>Mobile</dt><dd>${esc(n.mobile)}</dd></dl></aside></body></html>`;

before(async () => {
  needles = loadNeedles();
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    const html = body => { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(body); };
    // The record by its id: the product remembers it as recently viewed (a read with a side effect).
    if (u.pathname === '/contact/r1' || u.pathname === '/api/contacts/r1') { state.recent = true; return u.pathname.startsWith('/api') ? (res.writeHead(200), res.end('{}')) : html(RECORD(needles.contact)); }
    if (u.pathname === '/api/arm') { state.armed = true; res.writeHead(200); return res.end('{}'); }
    if (u.pathname === '/api/notifications') { state.polls++; res.writeHead(200); return res.end(state.armed && state.polls > 15 ? 'open' : 'none'); }
    if (u.pathname === '/contact') {
      const n = u.searchParams.get('n') || '';
      const whole = letters(n) === letters(needles.contact.name);
      // Best match first: a remembered record that the search matches comes first.
      if (whole || (state.recent && isPiece(n, needles.contact.name))) return html(RECORD(needles.contact));
      return html('<!doctype html><p>2,341 matches</p>');
    }
    if (u.pathname === '/contacts') return html(LIST());
    return html(HOME());
  });
  server.keepAliveTimeout = 1;
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-critic-r14-'));
});
after(() => { server?.close(); server?.closeAllConnections?.(); fs.rmSync(tmp, { recursive: true, force: true }); });

const TASK = { id: 'plant-find-r14', title: 'Plant (find one record)', startAt: 'home', moments: [], input: {}, data: ['contact.name', 'contact.mobile'], finds: 'contact.name', fixtures: { ours: [] } };
const standIn = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'admin', password: 'x' } }, brandWords: [], readyKind: 'default' });
const out = () => layout(path.join(tmp, String(Math.random()).slice(2)));
const runDriver = async driver => execute(TASK, await sandboxed(driver, { base }), standIn(), 'ours', needles, out(), { timeout: 15_000 });
const verifyRecord = async ctx => {
  const { name, mobile } = ctx.needles.contact;
  const shown = await ctx.read(([n, m]) => { const a = document.querySelector('aside'); return !!a && a.innerText.includes(n) && a.innerText.includes(m); }, [name, mobile]);
  return { verified: shown };
};
const ODOO = JSON.parse(fs.readFileSync(new URL('../../reference/odoo/tasks/find-record.json', import.meta.url), 'utf8')).counts;
const line = (name, r) => {
  const vs = r.status === 'verified' ? compareRuns(r, { status: 'verified', task: 'plant', counts: ODOO }) : null;
  return `RESULT ${name}: status=${r.status} steps=${r.counts?.steps} keys=${r.counts?.keystrokes} machine=${r.counts?.machine_seconds} typed=${JSON.stringify((r.steps || []).filter(s => s.kind === 'type').map(s => s.text))} finds=${JSON.stringify(r.finds)} screen=${JSON.stringify(r.screen_changes && { judged: r.screen_changes.judged, not_caused: r.screen_changes.not_caused })} set_up_writes=${JSON.stringify(r.set_up_writes)} vs_odoo_find_record=${vs?.verdict ?? ''} error=${r.error ?? ''}`;
};
const plant = (name, r) => {
  console.log(line(name, r));
  assert.notEqual(r.status, 'verified', `MISSED (${name}): verified with ${r.counts?.steps} steps, ${r.counts?.keystrokes} keys, ${r.counts?.machine_seconds} s; the record was put within reach by set-up`);
};
const control = (name, r, expect) => { console.log(line(name, r)); assert.equal(r.status, expect, r.error); };
const home = { async signIn(ctx) { await ctx.page.goto(base + '/'); } };
const twoLettersEnter = {
  async run(op, ctx) {
    await op.click('#contacts', { label: 'Contacts' });
    await op.waitFor(() => document.activeElement && document.activeElement.id === 'q', { label: 'list' });
    await op.type(ctx.needles.contact.name.slice(0, 2).toLowerCase(), { label: 'two letters' });
    await op.press('Enter', { label: 'best match' });
    await op.waitFor(() => !!document.querySelector('aside'), { label: 'record', timeout: 6000 });
    return {};
  },
  verify: verifyRecord,
};

test('critic r14 control C0: the honest whole-name search verifies', async () => {
  reset();
  control('C0', await runDriver({
    ...home,
    async run(op, ctx) {
      await op.click('#contacts', { label: 'Contacts' });
      await op.waitFor(() => document.activeElement && document.activeElement.id === 'q', { label: 'list' });
      await op.type(ctx.needles.contact.name, { label: 'name' });
      await op.press('Enter', { label: 'open' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record' });
      return {};
    },
    verify: verifyRecord,
  }), 'verified');
});

test('critic r14 control C1: two letters and Enter without set-up\'s visit find nothing', async () => {
  reset();
  const r = await runDriver({ ...home, ...twoLettersEnter });
  console.log(line('C1', r));
  assert.notEqual(r.status, 'verified');
});

test('critic r14 plant P1: set-up\'s browser opened the record by id (a GET); two letters and Enter open it -> never verifies', async () => {
  reset();
  plant('P1', await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/contact/r1'); await ctx.page.goto(base + '/'); },
    ...twoLettersEnter,
  }));
});

test('critic r14 plant P1b: the harness\'s fetch read the record by id in set-up; two letters and Enter open it -> never verifies', async () => {
  reset();
  plant('P1b', await runDriver({
    async setup() { await fetch(base + '/api/contacts/r1'); },
    ...home,
    ...twoLettersEnter,
  }));
});

test('critic r14 plant P2: the remembered record is a link on the start screen whose name CSS draws; two letters typed in a note, then the link -> never verifies', async () => {
  reset();
  state.cssRecent = true;
  plant('P2', await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/contact/r1'); await ctx.page.goto(base + '/'); },
    async run(op, ctx) {
      await op.click('#note', { label: 'note' });
      await op.type(ctx.needles.contact.name.slice(0, 2).toLowerCase(), { label: 'two letters' });
      await op.click('#recent', { label: 'recently viewed' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record' });
      return {};
    },
    verify: verifyRecord,
  }));
});

const pollPath = {
  ...home,
  async run(op, ctx) {
    await op.click('#contacts', { label: 'Contacts' });
    await op.waitFor(() => document.activeElement && document.activeElement.id === 'q', { label: 'list' });
    await op.type(ctx.needles.contact.name.slice(0, 2).toLowerCase(), { label: 'two letters' });
    await op.waitFor(() => !!document.querySelector('aside'), { label: 'record', timeout: 8000 });
    return {};
  },
  verify: verifyRecord,
};

test('critic r14 control C3: P3\'s path without set-up\'s arming does not open the record', async () => {
  reset();
  state.poll = true;
  const r = await runDriver(pollPath);
  console.log(line('C3', r));
  assert.notEqual(r.status, 'verified');
});

test('critic r14 plant P3: set-up armed the back end (a GET); the list\'s poll, started by the click, opens the record when the server says -> never verifies', async () => {
  reset();
  state.poll = true;
  plant('P3', await runDriver({ async setup() { await fetch(base + '/api/arm'); }, ...pollPath }));
});
