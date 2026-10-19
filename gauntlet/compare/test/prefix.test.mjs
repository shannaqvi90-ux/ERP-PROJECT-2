// Round 13 (lead decision on critic p06 r7's question, gauntlet/compare/README.md "A fixed prefix a
// field shows"): a fixed prefix a field shows as its own (+971 in a UAE phone field) need not be typed,
// but only when all of these hold:
//   - the task declares it for that product's field, as a constant the product's code sets for every
//     user and record of that kind (test/tasks.test.mjs checks the declaration against the code);
//   - the field shows it as the clock starts (read by the runner, recorded as prefixes_at_start);
//   - the rest of the value is typed in full, and the end state holds the whole value, the prefix as
//     written (its +) included;
//   - the same holds for the reference (a prefix its field shows would be declared the same way).
// Plants (each must never verify): PF1 set-up seeds a longer start ("+971 50 555") so the person
// types only "0199"; PF2 a prefix the task does not declare; PF3 a prefix the page shows only once
// the clock runs (an empty field that offers +971 on focus); PF4 a back end that drops the +.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { afterPrefix, execute, layout, measuredPartProblem, prefixProblems, showsPrefix } from '../lib/runner.mjs';
import { loadNeedles } from '../data/generate.mjs';
import { sandboxed } from './helpers/driver-module.mjs';

test('a prefix is shown when the field begins with it; the rest of a value is what follows it', () => {
  assert.equal(showsPrefix('+971 4 555 0100', '+971'), true);
  assert.equal(showsPrefix('+9714 555 0100', '+971'), true);
  assert.equal(showsPrefix('971 4 555 0100', '+971'), false, 'without its +');
  assert.equal(showsPrefix('+44 20 7946 0958', '+971'), false);
  assert.equal(showsPrefix('', '+971'), false);
  assert.equal(showsPrefix(null, '+971'), false);
  assert.equal(afterPrefix('+971 50 555 0199', '+971'), '50 555 0199');
  assert.equal(afterPrefix('+971505550199', '+971'), '505550199');
  assert.equal(afterPrefix('+44 20 7946 0958', '+971'), null);
  assert.equal(afterPrefix('+971', '+971'), null, 'nothing after it');
});

const TASK = {
  id: 'plant-prefix', title: 'Plant', startAt: 'record', moments: [], saves: true, enters: ['phone'], input: { phone: '+971 50 555 0199' },
  endState: { ours: { reads: [{ read: 'GET /api/phone', parts: ['phone'] }], writes: ['POST /api/phone'] } },
  prefixes: { ours: [{ field: '#phone', prefix: '+971' }] },
};
const typed = text => [{ n: 1, kind: 'click', label: 'phone', at: 0, took: 0.1 }, { n: 2, kind: 'type', text, at: 0.2, took: 0.1 }];
const sent = [{ method: 'POST', url: 'http://x/api/phone', body: '+971 50 555 0199', at: 0.25 }];

test('measured part: a declared prefix shown at the start need not be typed; the rest must be, and the end state must hold the whole value', () => {
  const judge = (text, prefixes, endText = '+971 50 555 0199') => measuredPartProblem(TASK, { steps: typed(text), sent, gained: ['+971 50 555 0199'], productId: 'ours', prefixes, endText });
  assert.equal(judge('505550199', ['+971']), null);
  assert.equal(judge('+971505550199', []), null, 'typed in full needs no prefix');
  assert.match(judge('505550199', []), /never entered/, 'PF2/PF3: no prefix shown or declared');
  assert.match(judge('0199', ['+971']), /never entered/, 'PF1: only the end of the number typed');
  assert.match(judge('5550199', ['+971']), /never entered/);
  assert.match(judge('505550199', ['+971'], '971505550199'), /never entered/, 'PF4: the end state lost the +');
  assert.match(judge('505550199', ['+97']), /never entered/, 'a prefix that is not the value\'s start');
});

test('a prefix declaration names a dialling code its product\'s code sets as a constant; one set-up, the data or the reference makes is refused', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-prefix-'));
  try {
    fs.mkdirSync(path.join(root, 'web'), { recursive: true });
    fs.writeFileSync(path.join(root, 'web', 'phone.ts'), 'export const HOME_PREFIX = "+971 ";\nexport const SEEDED = "+97150";\n');
    const declare = p => ({ ...TASK, prefixes: { ours: [{ field: '#phone', ...p }] } });
    assert.deepEqual(prefixProblems(declare({ prefix: '+971', source: 'web/phone.ts', constant: 'HOME_PREFIX' }), root), []);
    assert.match(prefixProblems(declare({ prefix: '+97150', source: 'web/phone.ts', constant: 'SEEDED' }), root).join(), /dialling code/, 'a longer start (set-up\'s)');
    assert.match(prefixProblems(declare({ prefix: '+971', source: 'web/phone.ts', constant: 'SEEDED' }), root).join(), /sets SEEDED to "\+97150", not the declared prefix/);
    assert.match(prefixProblems(declare({ prefix: '+971', source: 'web/phone.ts', constant: 'MISSING' }), root).join(), /declares no constant MISSING/);
    assert.match(prefixProblems(declare({ prefix: '+971' }), root).join(), /source must be a file of this repository/, 'no source: set-up or the data could have made it');
    assert.match(prefixProblems(declare({ prefix: '+971', source: '../../etc/hosts', constant: 'X' }), root).join(), /source must be a file of this repository/);
    assert.match(prefixProblems({ ...TASK, prefixes: { odoo: [{ field: 'input', prefix: '+971' }] } }, root).join(), /reference's fields needs a check against the running rig/);
    assert.match(prefixProblems({ ...declare({ prefix: '+44', source: 'web/phone.ts', constant: 'HOME_PREFIX' }) }, root).join(), /no value the task asks the person to enter begins with it/);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

// -- end to end on a stand-in product -------------------------------------------------------------

let server, base, tmp, needles;
let phone = '+971 4 555 0100';
let strip = false;
const PAGE = () => `<!doctype html><html><body><input id="phone" aria-label="Phone" value="${phone}"><button id="save">Save</button><div id="out"></div>
<script>const f = document.getElementById('phone');
// Like a field that keeps the country code: a click into it selects what follows "+971 ", and an empty
// field offers "+971 " once focused.
f.addEventListener('focus', () => { if (!f.value) f.value = '+971 '; });
f.addEventListener('mouseup', e => { if (f.value.startsWith('+971 ')) { f.setSelectionRange(5, f.value.length); e.preventDefault(); } });
document.getElementById('save').onclick = () => fetch('/api/phone', { method: 'POST', body: f.value }).then(() => { document.getElementById('out').textContent = 'saved'; });
</script></body></html>`;

before(async () => {
  needles = loadNeedles();
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    if (u.pathname === '/api/phone' && req.method === 'POST') {
      let body = '';
      req.on('data', c => { body += c; });
      req.on('end', () => {
        // Kept grouped after +971 (as our product stores it); PF4's back end drops the +.
        const digits = body.replace(/\D/g, '');
        phone = u.searchParams.has('raw') ? body : digits.startsWith('971') ? `${strip ? '' : '+'}971 ${digits.slice(3, 5)} ${digits.slice(5, 8)} ${digits.slice(8)}` : body;
        res.writeHead(204); res.end();
      });
      return;
    }
    if (u.pathname === '/api/phone') { res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ phone })); }
    res.writeHead(200, { 'Content-Type': 'text/html' });
    return res.end(PAGE());
  });
  server.keepAliveTimeout = 1;
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-prefix-'));
});
after(() => { server?.close(); server?.closeAllConnections?.(); fs.rmSync(tmp, { recursive: true, force: true }); });

const standIn = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'admin', password: 'x' } }, brandWords: [], readyKind: 'default' });
const runDriver = async (driver, task = TASK) => execute(task, await sandboxed(driver, { base }), standIn(), 'ours', needles, layout(path.join(tmp, String(Math.random()).slice(2))), { timeout: 10_000 });
const verifyPhone = async ctx => ({ verified: (await (await fetch(base + '/api/phone')).json()).phone.replace(/[\s+]/g, '') === ctx.task.input.phone.replace(/[\s+]/g, '') });
const driver = (start, digits) => ({
  setup: new Function('ctx', `return fetch(base + '/api/phone?raw=1', { method: 'POST', body: ${JSON.stringify(start)} });`), // eslint-disable-line no-new-func
  async signIn(ctx) { await ctx.page.goto(base + '/record'); },
  run: new Function('op', `return (async () => { await op.click('#phone'); await op.type(${JSON.stringify(digits)}); await op.click('#save'); await op.waitFor(() => document.getElementById('out').textContent === 'saved'); return {}; })();`), // eslint-disable-line no-new-func
  verify: verifyPhone,
});
const never = (name, r) => assert.equal(r.status, 'invalid', `${name}: ${r.status} with ${r.counts?.keystrokes} keys: ${r.error}`);

test('control: the field shows +971 at the start; the person types the 9 digits after it and the whole number is saved (9 keystrokes)', async () => {
  strip = false;
  const r = await runDriver(driver('+971 4 555 0100', '505550199'));
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.counts.keystrokes, 9);
  assert.deepEqual(r.prefixes_at_start, [{ field: '#phone', prefix: '+971', shown: true }]);
});

test('plant PF1: set-up seeds a longer start ("+971 50 555") and the person types only the last 4 digits -> never verifies', async () => {
  strip = false;
  // The stand-in keeps the seeded start, so the click selects only after "+971 " and the typed digits
  // replace "50 555"; the honest-looking shortcut is typing what set-up did not seed.
  const r = await runDriver({ ...driver('+971 50 555', '0199'),
    run: new Function('op', "return (async () => { await op.click('#phone'); await op.press('End'); await op.type('0199'); await op.click('#save'); await op.waitFor(() => document.getElementById('out').textContent === 'saved'); return {}; })();") }); // eslint-disable-line no-new-func
  never('PF1', r);
});

test('plant PF2: a prefix the task does not declare -> never verifies', async () => {
  strip = false;
  const { prefixes, ...undeclared } = TASK;
  never('PF2', await runDriver(driver('+971 4 555 0100', '505550199'), undeclared));
});

test('plant PF3: the field is empty at the start and offers +971 only once the clock runs -> never verifies', async () => {
  strip = false;
  const r = await runDriver(driver('', '505550199'));
  never('PF3', r);
  assert.deepEqual(r.prefixes_at_start, [{ field: '#phone', prefix: '+971', shown: false }]);
});

test('plant PF4: the back end drops the + of the country code -> never verifies', async () => {
  strip = true;
  try { never('PF4', await runDriver(driver('+971 4 555 0100', '505550199'))); } finally { strip = false; }
});
