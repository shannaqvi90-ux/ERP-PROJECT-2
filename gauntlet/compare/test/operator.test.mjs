// The operator against a real browser and a small local page: counts, waits and blind shots.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { launch, newContext } from '../lib/browser.mjs';
import { Operator } from '../lib/operator.mjs';
import { Device } from '../lib/device.mjs';
import { MASK_COLOR, brandingFor } from '../lib/blind.mjs';

const PAGE = `<!doctype html><html><head><title>Odoo - Contacts</title><link rel="icon" href="data:,"></head>
<body style="margin:0;font:16px sans-serif">
  <div id="brand" style="position:absolute;left:0;top:0;width:200px;height:60px;background:#714B67;color:#fff">Odoo</div>
  <img id="logo" src="/logo.png" style="position:absolute;left:300px;top:0;width:100px;height:60px;background:#00ff00">
  <div data-brand style="position:absolute;left:450px;top:0;width:100px;height:60px;background:#0000ff"></div>
  <div id="accent" style="position:absolute;left:700px;top:300px;width:80px;height:80px;background:#e3342f"></div>
  <input id="q" style="position:absolute;left:0;top:100px">
  <button id="go" style="position:absolute;left:0;top:140px" onclick="setTimeout(()=>{document.getElementById('out').textContent='found '+document.getElementById('q').value},300)">Go</button>
  <div id="out" style="position:absolute;left:0;top:180px"></div>
  <input id="file" type="file" style="position:absolute;left:0;top:220px" onchange="document.getElementById('out').textContent='file '+this.files[0].name">
</body></html>`;

let server, base, browser, tmp;
before(async () => {
  server = http.createServer((req, res) => {
    if (req.url === '/logo.png') { res.writeHead(404); return res.end(); }
    if (req.url === '/csp') { res.writeHead(200, { 'Content-Type': 'text/html', 'Content-Security-Policy': "script-src 'self'" }); return res.end('<!doctype html><title>csp</title><p id="p">x</p>'); }
    res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(PAGE);
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}/`;
  browser = await launch();
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-op-'));
});
after(async () => { await browser?.close(); server?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });

async function fresh() {
  const context = await newContext(browser);
  const page = await context.newPage();
  await page.goto(base);
  return { context, page, op: new Operator(page, { shotsDir: path.join(tmp, 'shots'), branding: brandingFor('odoo'), shotFormat: 'png' }) };
}

test('steps, keystrokes and waits are counted the same way every time', async () => {
  const { context, op } = await fresh();
  op.start();
  await op.fill('#q', 'Abc', { label: 'query' });
  await op.press('Enter');
  await op.click('#go');
  await op.waitFor(() => document.getElementById('out').textContent.startsWith('found'), { label: 'result' });
  op.finish();
  const s = op.summary();
  assert.equal(s.steps, 4);
  assert.equal(s.clicks, 2);
  assert.equal(s.field_entries, 1);
  assert.equal(s.key_chords, 1);
  assert.equal(s.keystrokes, 5); // A (2 with Shift) + b + c + Enter
  assert.ok(s.system_wait_seconds >= 0.25, `waited ${s.system_wait_seconds}s for a 300 ms response`);
  assert.ok(s.machine_seconds >= s.system_wait_seconds);
  // M: one for the field click (typing into it is chained), Enter is chained, one for the button.
  assert.deepEqual(s.klm_operator_counts, { K: 5, P: 2, B: 4, H: 2, M: 2 });
  assert.equal(await op.page.inputValue('#q'), 'Abc');
  await context.close();
});

// Round 9 (critic p01 r8, mutations A1 and A26): a chord counts every key it presses, and a scroll
// with the wheel is a step of its own.
test('a key chord counts each of its keys, and a scroll is a step', async () => {
  const { context, page, op } = await fresh();
  await page.setContent('<div style="height:3000px"></div><button id="far">Far</button><input id="f">');
  op.start();
  await op.press('Control+a', { label: 'select all' });
  await op.press('Control+Shift+KeyZ', { label: 'redo' });
  await op.scrollTo('#far', { label: 'down to Far' });
  op.finish();
  const s = op.summary();
  assert.equal(s.steps, 3);
  assert.equal(s.key_chords, 2);
  assert.equal(s.scrolls, 1);
  assert.equal(s.keystrokes, 5, 'Control+a is 2 keys and Control+Shift+Z is 3');
  assert.deepEqual(op.steps.map(x => [x.kind, x.keystrokes]), [['key', 2], ['key', 3], ['scroll', 0]]);
  await context.close();
});

test('choosing a file counts the dialog click and the pick', async () => {
  const { context, op } = await fresh();
  const f = path.join(tmp, 'rows.csv');
  fs.writeFileSync(f, 'Name\nX\n');
  op.start();
  await op.pickFile('#file', f, { label: 'rows.csv' });
  await op.waitFor(() => document.getElementById('out').textContent === 'file rows.csv');
  op.finish();
  const s = op.summary();
  assert.equal(s.steps, 2);
  assert.equal(s.file_picks, 1);
  assert.equal(s.keystrokes, 0);
  await context.close();
});

test('a step before start() is refused', async () => {
  const { context, op } = await fresh();
  await assert.rejects(op.click('#go'), /start\(\)/);
  await context.close();
});

test('blind screenshots paint branding over, go grey, and leave title and favicon neutral', async () => {
  const { context, page, op } = await fresh();
  const shot = await op.shot('start');
  assert.match(shot.file, /^[0-9a-f]{16}\.png$/);
  assert.equal(await page.title(), 'Product');
  assert.equal(await page.locator('link[rel~="icon"]').count(), 0);
  // Read pixels back through a canvas in the same browser.
  const data = fs.readFileSync(path.join(tmp, 'shots', shot.file)).toString('base64');
  const probe = await context.newPage();
  const px = await probe.evaluate(async (src) => {
    const img = new Image(); img.src = src; await img.decode();
    const c = document.createElement('canvas'); c.width = img.width; c.height = img.height;
    const g = c.getContext('2d'); g.drawImage(img, 0, 0);
    const at = (x, y) => Array.from(g.getImageData(x, y, 1, 1).data.slice(0, 3));
    return { brand: at(100, 30), logo: at(350, 30), dataBrand: at(500, 30), plain: at(1000, 500), accent: at(740, 340) };
  }, `data:image/png;base64,${data}`);
  const mask = [1, 3, 5].map(i => parseInt(MASK_COLOR.slice(i, i + 2), 16));
  const grey = ([r, g, b]) => Math.max(r, g, b) - Math.min(r, g, b) <= 2;
  const near = (a, b) => a.every((v, i) => Math.abs(v - b[i]) <= 6);
  assert.ok(near(px.brand, mask), `brand text box painted over: ${px.brand}`);
  assert.ok(near(px.logo, mask), `logo painted over: ${px.logo}`);
  assert.ok(grey(px.plain), `page rendered in greyscale: ${px.plain}`);
  // Round 7 (critic mutation M2): a signature colour, not only a background that is grey anyway.
  assert.ok(grey(px.accent) && px.accent[0] < 200, `a coloured block rendered in greyscale: ${px.accent}`);
  // Our own product's branding hook: [data-brand].
  const ours = new Operator(page, { shotsDir: path.join(tmp, 'shots'), branding: brandingFor('ours'), shotFormat: 'png' });
  const s2 = await ours.shot('start');
  const d2 = fs.readFileSync(path.join(tmp, 'shots', s2.file)).toString('base64');
  const px2 = await probe.evaluate(async (src) => {
    const img = new Image(); img.src = src; await img.decode();
    const c = document.createElement('canvas'); c.width = img.width; c.height = img.height;
    const g = c.getContext('2d'); g.drawImage(img, 0, 0);
    return Array.from(g.getImageData(500, 30, 1, 1).data.slice(0, 3));
  }, `data:image/png;base64,${d2}`);
  assert.ok(near(px2, mask), `[data-brand] painted over: ${px2}`);
  await context.close();
});

test('a screenshot after the end of a task never shortens the measured time', async () => {
  const { context, op } = await fresh();
  op.start();
  // Timers may fire a fraction of a millisecond early on a busy machine; wait until 30 ms have
  // really passed on the operator's own clock, so the assertion below tests the operator only.
  const begun = performance.now();
  while (performance.now() - begun < 31) await new Promise(r => setTimeout(r, 5));
  op.finish();
  const measured = op.machineSeconds;
  await op.shot('done');
  assert.equal(op.machineSeconds, measured);
  assert.ok(measured >= 0.03, `measured ${measured}s`);
  await context.close();
});

test('round 3 fault T1: a screenshot taken while measuring stays on the clock (it cannot hide the product working)', async () => {
  const { context, op } = await fresh();
  op.start();
  const t0 = performance.now();
  await op.shot('a'); await op.shot('b'); await op.shot('c');
  const wall = (performance.now() - t0) / 1000;
  op.finish();
  assert.ok(op.machineSeconds >= wall - 0.002, `three shots took ${wall}s of wall time but the clock shows ${op.machineSeconds}s`);
  await context.close();
});

test('round 2 fault: the done screenshot after finish() leaves machine seconds unchanged', async () => {
  const { context, op } = await fresh();
  op.start();
  await op.fill('#q', 'x');
  await op.click('#go');
  await op.waitFor(() => document.getElementById('out').textContent.startsWith('found'), { label: 'result' });
  op.finish();
  const before = op.machineSeconds;
  await op.shot('done'); await op.shot('done again');
  assert.equal(op.machineSeconds, before, 'a shot after the clock stopped moved it');
  const s = op.summary();
  const lastEnd = Math.max(...op.steps.map(x => x.at + x.took), ...op.waits.map(w => w.at + w.seconds));
  assert.ok(s.machine_seconds + 0.002 >= lastEnd, `machine ${s.machine_seconds} < end of the last step or wait ${lastEnd}`);
  assert.ok(s.machine_seconds - lastEnd < 0.1, `machine ${s.machine_seconds} far beyond the last step or wait ${lastEnd}`);
  assert.ok(s.machine_seconds >= s.system_wait_seconds, 'waits are part of the clock');
  await context.close();
});

test('while measuring, only the moments the task declares are shot, each once; their time stays on the clock', async () => {
  const { context, page } = await fresh();
  const op = new Operator(page, { shotsDir: path.join(tmp, 'shots'), branding: brandingFor('odoo'), shotFormat: 'png', moments: ['result list'] });
  op.start();
  await op.click('#go');
  await assert.rejects(op.shot('progress'), /does not declare/);
  assert.deepEqual(op.missingMoments, ['result list']);
  const t = op.now();
  await op.shot('result list');
  assert.ok(op.now() - t > 0, 'the shot took time on the clock');
  await assert.rejects(op.shot('result list'), /once/);
  assert.deepEqual(op.missingMoments, []);
  await op.press('Tab');
  op.finish();
  const [first, second] = op.steps;
  assert.ok(second.at >= first.at + first.took, 'steps after the shot start after it');
  await op.shot('done');
  await context.close();
});

test('round 3 fault S1: op.type refuses control characters (a newline would press Enter without a step)', async () => {
  const { context, op } = await fresh();
  op.start();
  await op.click('#q');
  for (const text of ['y\n', 'a\rb', 'x\ty', 'ab\b', '\u0003', '\u007f']) {
    await assert.rejects(op.type(text), /control character/, JSON.stringify(text));
  }
  await assert.rejects(op.press('a\n'), /not a key/);
  await op.type('plain text, digits 12 and symbols @#!');
  op.finish();
  assert.equal(op.steps.length, 2, 'refused entries record no step');
  await context.close();
});

test('round 3 fault K1: a driver cannot declare continuation (chain) or pass options that act uncounted', async () => {
  const { context, op } = await fresh();
  op.start();
  await assert.rejects(op.click('#q', { chain: true }), /derived by the instrument/);
  await assert.rejects(op.type('x', { chain: true }), /derived by the instrument/);
  await assert.rejects(op.press('Enter', { chain: true }), /derived by the instrument/);
  await assert.rejects(op.click('#go', { modifiers: ['Shift'] }), /not an option/);
  await assert.rejects(op.click('#go', { clickCount: 3 }), /not an option/);
  await assert.rejects(op.fill('#q', 'x', { chain: true }), /derived by the instrument/);
  // Derived: the click on the field and typing into it are one unit; Enter after typing too.
  await op.click('#q');
  await op.type('abc');
  await op.press('Enter');
  await op.click('#go');
  await op.type('z');
  op.finish();
  assert.deepEqual(op.steps.map(x => x.chain), [false, true, true, false, false], 'typing after a click on a button (not a field) is not a continuation');
  await context.close();
});

test('an API request is one step whose keystrokes are the request typed plus Enter', async () => {
  const { context, op } = await fresh();
  op.useApi({ baseUrl: base });
  op.start();
  const r = await op.request('GET', '/x?q=Ab');
  op.finish();
  assert.equal(r.status, 200);
  const s = op.summary();
  assert.equal(s.steps, 1);
  assert.equal(s.requests, 1);
  assert.equal(s.keystrokes, 'GET /x?q=Ab'.length + 5 /* G, E, T, ?, A need Shift */ + 1 /* Enter */);
  assert.ok(s.system_wait_seconds > 0 && s.machine_seconds >= s.system_wait_seconds);
  assert.deepEqual(s.klm_operator_counts, { K: s.keystrokes, P: 0, B: 0, H: 1, M: 1 });
  await context.close();
});

test('a wait condition works on a page whose content security policy forbids eval (our product)', async () => {
  const { context, page, op } = await fresh();
  await page.goto(base + 'csp');
  op.start();
  setTimeout(() => { page.evaluate(() => { document.getElementById('p').textContent = 'ready'; }).catch(() => {}); }, 200);
  const w = await op.waitFor(() => document.getElementById('p').textContent === 'ready', { label: 'ready', timeout: 5000 });
  op.finish();
  assert.ok(w.seconds >= 0.1, `waited ${w.seconds}s`);
  await context.close();
});

test('round 5: paste and copy chords are read as the keys they press, whatever their spelling', async () => {
  const { isPaste, isCopy, parseChord } = await import('../lib/operator.mjs');
  for (const c of ['Control+v', 'control+V', 'ControlOrMeta+v', 'Meta+v', 'Control+KeyV', 'Shift+Control+v', 'Control+Shift+V', 'Alt+Control+v', 'Shift+Insert', 'Ctrl+v'])
    assert.ok(isPaste(c), `${c} pastes`);
  for (const c of ['v', 'Shift+v', 'Alt+v', 'Control+Shift+Insert', 'Insert', 'Control+c']) assert.ok(!isPaste(c), `${c} does not paste`);
  for (const c of ['Control+c', 'ControlOrMeta+c', 'control+X', 'Meta+c', 'Control+KeyC', 'Control+Insert', 'Shift+Delete']) assert.ok(isCopy(c), `${c} copies`);
  for (const c of ['c', 'Control+v', 'Delete', 'Shift+Insert']) assert.ok(!isCopy(c), `${c} does not copy`);
  assert.deepEqual([...parseChord('ControlOrMeta+Shift+KeyV').mods].sort(), [process.platform === 'darwin' ? 'meta' : 'control', 'shift'].sort());
  assert.equal(parseChord('Control++').key, '+');
});

test('a screenshot the browser fails to take ends the run: it is tried once, never again on the clock (round 6)', async () => {
  // A second try inside the measured part would charge one product time the other does not pay.
  const { context, page } = await fresh();
  let tries = 0;
  const failing = new Proxy(page, {
    get(target, key) {
      if (key === 'screenshot') return async () => { tries++; throw new Error('page.screenshot: Protocol error (Page.captureScreenshot): Unable to capture screenshot'); };
      const v = Reflect.get(target, key, target);
      return typeof v === 'function' ? v.bind(target) : v;
    },
  });
  const op = new Operator(failing, { shotsDir: path.join(tmp, 'shots-fail'), branding: brandingFor('odoo'), moments: ['result'], shotFormat: 'png' });
  op.start();
  await assert.rejects(op.shot('result'), /Unable to capture screenshot/);
  assert.equal(tries, 1, 'the failed shot was taken again');
  op.finish();
  assert.deepEqual(op.shots, [], 'a failed shot is not recorded');
  await context.close();
});

// The person's passkey device (lib/device.mjs): a page served from "localhost" (a passkey needs a
// domain), which makes a passkey and later asks for it.
const PASSKEY_PAGE = `<!doctype html><title>passkey</title><body><script>
const bytes = n => new Uint8Array(n).map((_, i) => i + 1);
window.make = () => navigator.credentials.create({ publicKey: { challenge: bytes(16), rp: { id: 'localhost', name: 'x' }, user: { id: bytes(8), name: 'a', displayName: 'a' },
  pubKeyCredParams: [{ type: 'public-key', alg: -7 }], authenticatorSelection: { residentKey: 'required', userVerification: 'required' } } }).then(c => c.id);
const say = text => { document.body.dataset.result = text; };
window.ask = () => { say('waiting'); navigator.credentials.get({ publicKey: { challenge: bytes(16), rpId: 'localhost', userVerification: 'required', allowCredentials: [] } })
  .then(c => say('signed in ' + c.id), e => say('refused ' + e.name)); };
</script></body>`;

async function passkeyPage(device, { timeout } = {}) {
  const local = base.replace('127.0.0.1', 'localhost') + 'passkey';
  const context = await newContext(browser);
  if (device) await device.attachContext(context);
  const page = await context.newPage();
  if (device) await device.attachPage(page);
  await page.route('**/passkey', r => r.fulfill({ status: 200, contentType: 'text/html', body: PASSKEY_PAGE }));
  await page.goto(local);
  const op = new Operator(page, { shotsDir: path.join(tmp, 'shots'), branding: brandingFor('odoo'), shotFormat: 'png', device, ...(timeout ? { defaultTimeout: timeout } : {}) });
  return { context, page, op };
}

test('passkey: set-up makes a passkey at once; measured, the request waits for the person, and confirming is one step with no keystroke', async () => {
  const device = new Device();
  const { context, page, op } = await passkeyPage(device);
  // Set-up (the free phase): the device confirms at once.
  const id = await page.evaluate(() => window.make());
  assert.ok(id);
  assert.equal(await device.collect(), 1);
  op.start();
  await page.evaluate(() => window.ask());
  await new Promise(r => setTimeout(r, 300));
  assert.equal(await page.evaluate(() => document.body.dataset.result), 'waiting', 'the product was answered before the person confirmed');
  assert.equal(device.waiting(page), 1);
  const step = await op.confirmOnDevice({ label: 'confirm' });
  await op.waitFor(() => String(document.body.dataset.result).startsWith('signed in'), { label: 'signed in' });
  op.finish();
  assert.equal(step.kind, 'device');
  assert.equal(step.ceremony, 'get');
  const s = op.summary();
  assert.equal(s.steps, 1);
  assert.equal(s.keystrokes, 0);
  assert.equal(s.device_confirmations, 1);
  // M, then H to reach the device (the hand starts on the mouse), then one button press.
  assert.deepEqual(s.klm_operator_counts, { K: 1, P: 0, B: 0, H: 1, M: 1 });
  assert.equal(s.human_seconds, 2.03);
  await context.close();
});

test('passkey: the device follows the person to a fresh browser, and the page cannot answer for the person', async () => {
  const device = new Device();
  const first = await passkeyPage(device);
  await first.page.evaluate(() => window.make());
  await device.collect();
  await first.context.close();
  const { context, page, op } = await passkeyPage(device);
  op.start();
  // The page reaches neither an unheld get nor the release: calling the prototype directly is held too.
  await page.evaluate(() => {
    document.body.dataset.result = 'waiting';
    CredentialsContainer.prototype.get.call(navigator.credentials, { publicKey: { challenge: new Uint8Array(16), rpId: 'localhost', userVerification: 'required' } })
      .then(() => { document.body.dataset.result = 'signed in'; }, e => { document.body.dataset.result = 'refused ' + e.name; });
  });
  await new Promise(r => setTimeout(r, 300));
  assert.equal(await page.evaluate(() => document.body.dataset.result), 'waiting');
  await op.confirmOnDevice();
  await op.waitFor(() => document.body.dataset.result === 'signed in', { label: 'signed in with the carried passkey' });
  op.finish();
  await context.close();
});

test('passkey: confirming is refused without a device, and fails when the product never asks', async () => {
  const none = await passkeyPage(null);
  none.op.start();
  await assert.rejects(none.op.confirmOnDevice(), /declares no device/);
  none.op.finish();
  await none.context.close();
  const { context, op } = await passkeyPage(new Device(), { timeout: 400 });
  op.start();
  await assert.rejects(op.confirmOnDevice(), /did not ask the device/);
  op.finish();
  await context.close();
});

