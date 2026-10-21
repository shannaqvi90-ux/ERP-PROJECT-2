// Critic p01 round 13 plants (fresh critic). Stand-in product, same scaffolding as the round 12
// critic's plants and test/causation.test.mjs. Each plant must never be recorded as 'verified'.
//
// Every causal rule since round 11 (the browser's trace, the start gap after verify()'s read, the
// before-clock read) is applied only to a task that SAVES. Eleven of the 24 tasks do not save
// (find-record, which is a named task, find-user, switch-company, export-filtered-list,
// who-changed-field, the keyboard tasks, the Arabic reports). For them the only guards are the start
// state (fields, address, home landing), "not done before the clock" and "at least one step".
// These plants are the off-the-clock class of rounds 10-12 (R2, S1, W1) moved to a task that does not
// save:
//
//   N1  set-up turns on a setting ("open the last record at start"). The start page (a list), on
//       its own timer, opens the task's record a moment after it loads. run() = one Escape and a
//       wait for the record. The person found nothing.
//   N1b the same with one click on plain text.
//   N2  set-up opens the task's record (the product remembers it server side as "recently
//       viewed"; the browser's own storage is cleared by the runner, the server's is not). The start
//       list shows "Recently viewed: <the needle's name>" as a link (text, not a field, and not in
//       the address, so no start check sees it). run() = one click on it. The needle is never typed.
//   C1  control: the honest path (type the name in the search box, Enter, the record opens) verifies.
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
let openAtStart = false;
let recent = null;

const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

const LIST = () => `<!doctype html><html><body>
<nav aria-label="Main navigation">Contacts</nav>
<input type="search" id="q" aria-label="Search">
${recent ? `<p>Recently viewed: <a id="recent" href="/contact">${esc(recent.name)}</a></p>` : ''}
<div id="rows">${Array.from({ length: 20 }, (_, i) => `<div class="row">Contact ${i + 1}</div>`).join('')}</div>
<script>
const q = document.getElementById('q');
q.addEventListener('keydown', e => { if (e.key === 'Enter' && q.value.trim()) location.href = '/contact?n=' + encodeURIComponent(q.value.trim()); });
${openAtStart ? "setTimeout(() => { location.href = '/contact'; }, 1600);" : ''}
</script></body></html>`;

const RECORD = n => `<!doctype html><html><body><nav aria-label="Main navigation">Contacts</nav>
<aside id="record"><h1>${esc(n.name)}</h1><dl><dt>Mobile</dt><dd>${esc(n.mobile)}</dd></dl></aside></body></html>`;

before(async () => {
  needles = loadNeedles();
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    if (u.pathname === '/api/settings' && req.method === 'POST') { openAtStart = true; res.writeHead(204); return res.end(); }
    if (u.pathname === '/contact') {
      const n = u.searchParams.get('n');
      if (n !== null && n.toLowerCase() !== needles.contact.name.toLowerCase()) { res.writeHead(200, { 'Content-Type': 'text/html' }); return res.end('<!doctype html><p>No match</p>'); }
      recent = needles.contact; // the server remembers the record as recently viewed
      res.writeHead(200, { 'Content-Type': 'text/html' });
      return res.end(RECORD(needles.contact));
    }
    res.writeHead(200, { 'Content-Type': 'text/html' });
    return res.end(LIST());
  });
  server.keepAliveTimeout = 1;
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-critic-r13-'));
});
after(() => {
  server?.close(); server?.closeAllConnections?.();
  fs.rmSync(tmp, { recursive: true, force: true });
});

// A find-record-shaped task: it does not save; the person opens one contact among many.
const TASK = { id: 'plant', title: 'Plant (find one record)', startAt: 'list', moments: [], input: {}, data: ['contact.name', 'contact.mobile'] };
const standIn = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'admin', password: 'x' } }, brandWords: [], readyKind: 'default' });
const out = () => layout(path.join(tmp, String(Math.random()).slice(2)));
const runDriver = async driver => execute(TASK, await sandboxed(driver, { base }), standIn(), 'ours', needles, out(), { timeout: 10_000 });
const reset = () => { openAtStart = false; recent = null; };
// Odoo's recorded find-record baseline (gauntlet/reference/odoo/tasks/find-record.json).
const ODOO_FIND_RECORD = JSON.parse(fs.readFileSync(new URL('../../reference/odoo/tasks/find-record.json', import.meta.url), 'utf8')).counts;
const verifyRecord = async ctx => {
  const { name, mobile } = ctx.needles.contact;
  const shown = await ctx.read(([n, m]) => { const a = document.querySelector('aside'); return !!a && a.innerText.includes(n) && a.innerText.includes(m); }, [name, mobile]);
  return { verified: shown };
};
const report = (name, r) => {
  const vs = r.status === 'verified' ? compareRuns(r, { status: 'verified', task: 'plant', counts: ODOO_FIND_RECORD }) : null;
  console.log(`RESULT ${name}: status=${r.status} steps=${r.counts?.steps} keys=${r.counts?.keystrokes} machine=${r.counts?.machine_seconds} human=${r.counts?.human_seconds} human_plus_wait=${r.counts?.human_plus_wait_seconds} typed=${JSON.stringify((r.steps || []).filter(s => s.kind === 'type').map(s => s.text))} start=${JSON.stringify(r.start_state?.path)} vs_odoo_find_record=${vs?.verdict} ${vs ? JSON.stringify(vs.metrics ?? vs) : ''} error=${r.error ?? ''}`);
  assert.notEqual(r.status, 'verified', `MISSED (${name}): verified with ${r.counts?.steps} steps, ${r.counts?.keystrokes} keys, ${r.counts?.machine_seconds} s and nothing found by the person; against Odoo's find-record baseline the comparison says ${vs?.verdict}`);
};

test('critic r13 control C1: the honest search (type the name, Enter) verifies', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/contacts'); },
    async run(op, ctx) {
      await op.click('#q', { label: 'search' });
      await op.type(ctx.needles.contact.name, { label: 'name' });
      await op.press('Enter', { label: 'open' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record' });
      return {};
    },
    verify: verifyRecord,
  });
  console.log(`RESULT C1: status=${r.status} steps=${r.counts?.steps} keys=${r.counts?.keystrokes} machine=${r.counts?.machine_seconds} error=${r.error ?? ''}`);
  assert.equal(r.status, 'verified', r.error);
});

test('critic r13 plant N1: set-up turns on "open the last record at start"; the start list opens the record on its own timer; run() presses Escape once -> never verifies', async () => {
  reset();
  report('N1', await runDriver({
    async setup() { await fetch(base + '/api/settings', { method: 'POST' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/contacts'); },
    async run(op) {
      await op.press('Escape', { label: 'dismiss' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record', timeout: 9000 });
      return {};
    },
    verify: verifyRecord,
  }));
});

test('critic r13 plant N1b: the same with one click on plain text -> never verifies', async () => {
  reset();
  report('N1b', await runDriver({
    async setup() { await fetch(base + '/api/settings', { method: 'POST' }); },
    async signIn(ctx) { await ctx.page.goto(base + '/contacts'); },
    async run(op) {
      await op.click('#rows', { label: 'list' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record', timeout: 9000 });
      return {};
    },
    verify: verifyRecord,
  }));
});

test('critic r13 plant N2: set-up opens the record (the server lists it as recently viewed on the start list); run() clicks that link once, typing nothing -> never verifies', async () => {
  reset();
  report('N2', await runDriver({
    async signIn(ctx) {
      await ctx.page.goto(base + '/contact?n=' + encodeURIComponent(ctx.needles.contact.name));
      await ctx.page.goto(base + '/contacts');
    },
    async run(op) {
      await op.click('#recent', { label: 'recently viewed' });
      await op.waitFor(() => !!document.querySelector('aside'), { label: 'record' });
      return {};
    },
    verify: verifyRecord,
  }));
});
