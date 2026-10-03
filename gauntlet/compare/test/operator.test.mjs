// The operator against a real browser and a small local page: counts, waits and blind shots.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { launch, newContext } from '../lib/browser.mjs';
import { Operator } from '../lib/operator.mjs';
import { MASK_COLOR, brandingFor } from '../lib/blind.mjs';

const PAGE = `<!doctype html><html><head><title>Odoo - Contacts</title><link rel="icon" href="data:,"></head>
<body style="margin:0;font:16px sans-serif">
  <div id="brand" style="position:absolute;left:0;top:0;width:200px;height:60px;background:#714B67;color:#fff">Odoo</div>
  <img id="logo" src="/logo.png" style="position:absolute;left:300px;top:0;width:100px;height:60px;background:#00ff00">
  <div data-brand style="position:absolute;left:450px;top:0;width:100px;height:60px;background:#0000ff"></div>
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
  await op.press('Enter', { chain: true });
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
    return { brand: at(100, 30), logo: at(350, 30), dataBrand: at(500, 30), plain: at(1000, 500) };
  }, `data:image/png;base64,${data}`);
  const mask = [1, 3, 5].map(i => parseInt(MASK_COLOR.slice(i, i + 2), 16));
  const grey = ([r, g, b]) => Math.max(r, g, b) - Math.min(r, g, b) <= 2;
  const near = (a, b) => a.every((v, i) => Math.abs(v - b[i]) <= 6);
  assert.ok(near(px.brand, mask), `brand text box painted over: ${px.brand}`);
  assert.ok(near(px.logo, mask), `logo painted over: ${px.logo}`);
  assert.ok(grey(px.plain), `page rendered in greyscale: ${px.plain}`);
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
  await new Promise(r => setTimeout(r, 30));
  op.finish();
  const measured = op.machineSeconds;
  await op.shot('done');
  assert.equal(op.machineSeconds, measured);
  assert.ok(measured >= 0.03, `measured ${measured}s`);
  await context.close();
});

test('screenshot time is not counted as product time', async () => {
  const { context, op } = await fresh();
  op.start();
  const t0 = op.machineSeconds;
  await op.shot('a'); await op.shot('b'); await op.shot('c');
  assert.ok(op.machineSeconds - t0 < 0.05, `three shots added ${op.machineSeconds - t0}s`);
  op.finish();
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

test('a shot in the middle of the measured part is taken out of the clock and of later step times', async () => {
  const { context, op } = await fresh();
  op.start();
  await op.click('#go');
  const t = op.now();
  await op.shot('middle');
  assert.ok(op.now() - t < 0.05, `the shot moved the clock by ${op.now() - t}s`);
  await op.press('Tab');
  op.finish();
  const [, second] = op.steps;
  assert.ok(second.at - (op.steps[0].at + op.steps[0].took) < 0.05);
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
