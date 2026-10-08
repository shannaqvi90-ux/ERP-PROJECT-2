import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { BRANDING, assignLetters, blindName, brandingFor, neutralMoments, revealsProduct } from '../lib/blind.mjs';
import os from 'node:os';
import { writeReview } from '../lib/review.mjs';
import { BASELINE_DIR } from '../lib/config.mjs';

test('screenshot names are random and never name a product', () => {
  const names = new Set();
  for (let i = 0; i < 500; i++) {
    const n = blindName();
    assert.match(n, /^[0-9a-f]{16}\.jpg$/);
    assert.equal(revealsProduct(n), false);
    names.add(n);
  }
  assert.equal(names.size, 500);
});

test('revealsProduct catches product names in any case', () => {
  assert.ok(revealsProduct('ODOO-step1.png'));
  assert.ok(revealsProduct('ours_done.jpg'));
  assert.ok(revealsProduct('acme.png', ['Acme']));
});

test('both products have a branding profile; extra brand words are added', () => {
  assert.ok(BRANDING.odoo.words.includes('Odoo'));
  assert.ok(BRANDING.odoo.selectors.length >= 5);
  assert.ok(brandingFor('ours', ['Zeta']).words.includes('Zeta'));
  assert.throws(() => brandingFor('other'), /unknown product/);
});

test("the demo data's company names and codes are masked in both products (round 3 blindness finding)", () => {
  for (const p of ['odoo', 'ours']) {
    const b = brandingFor(p);
    assert.ok(b.identity.length >= 1, `${p}: company name`);
    assert.ok(b.identityExact.length >= 1, `${p}: database or tenant code`);
  }
  assert.ok(brandingFor('odoo').identity.includes('Demo Trading LLC'));
  assert.ok(brandingFor('ours').identity.includes('Al Noor Trading LLC'));
  assert.ok(brandingFor('ours').identityExact.includes('alnoor'));
  assert.ok(brandingFor('odoo').identityExact.includes('reference'));
});

test('letters A and B are assigned at random', () => {
  assert.deepEqual(assignLetters(['ours', 'odoo'], () => 0.1), { ours: 'A', odoo: 'B' });
  assert.deepEqual(assignLetters(['ours', 'odoo'], () => 0.9), { odoo: 'A', ours: 'B' });
});

test('committed Odoo baseline screenshots have neutral names and the key lives outside the shots folder', () => {
  const shots = path.join(BASELINE_DIR, 'shots');
  if (!fs.existsSync(shots)) return;
  for (const f of fs.readdirSync(shots)) {
    assert.equal(revealsProduct(f), false, f);
    assert.match(f, /^[0-9a-f]{16}\.(jpg|png)$/);
  }
  assert.ok(!fs.existsSync(path.join(shots, 'key.json')));
});

test('the blind review page shows neutral captions, never the moments a driver named', () => {
  assert.deepEqual(neutralMoments([{ moment: 'start' }, { moment: 'mapping preview' }, { moment: 'labels in Arabic' }, { moment: 'done' }]),
    ['start', 'moment 1', 'moment 2', 'done']);
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'review-'));
  const runs = {
    odoo: { status: 'verified', screenshots: [{ file: 'a1.jpg', moment: 'start' }, { file: 'a2.jpg', moment: 'labels in Arabic, layout still left to right' }, { file: 'a3.jpg', moment: 'done' }] },
    ours: { status: 'verified', screenshots: [{ file: 'b1.jpg', moment: 'start' }, { file: 'b2.jpg', moment: 'mapping preview' }, { file: 'b3.jpg', moment: 'done' }] },
  };
  const html = fs.readFileSync(writeReview(dir, [{ cmp: { task: 'switch-to-arabic' }, runs }]), 'utf8');
  assert.ok(!html.includes('left to right') && !html.includes('mapping preview'), 'driver moment names leak into the review page');
  assert.ok(html.includes('moment 1'));
  fs.rmSync(dir, { recursive: true, force: true });
});

test('a placeholder naming the vendor is cleared, not painted over', () => {
  assert.ok(!BRANDING.odoo.selectors.some(s => s.startsWith('[placeholder')), 'a mask over a filled-in field would single the product out');
});

test('a side-by-side folder keeps the blind part (shots, review page) apart from the key and results', async () => {
  const { layout, writeResult, NEUTRAL_FILE_TIME } = await import('../lib/runner.mjs');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'blind-layout-'));
  try {
    const out = layout(dir);
    assert.equal(out.shotsDir, path.join(dir, 'blind', 'shots'));
    assert.equal(path.dirname(out.keyFile), dir);
    assert.ok(!out.keyFile.startsWith(out.blindDir) && !out.resultsDir.startsWith(out.blindDir));
    fs.mkdirSync(out.shotsDir, { recursive: true });
    const files = ['1111111111111111.jpg', '2222222222222222.jpg'];
    for (const f of files) fs.writeFileSync(path.join(out.shotsDir, f), 'x');
    writeResult({ task: 'find-record', product: 'odoo', run_id: 'r1', screenshots: files.map((file, i) => ({ file, moment: i ? 'done' : 'start' })) }, out);
    // File times are neutral: nothing about the order of the runs survives in them.
    for (const f of files) assert.equal(fs.statSync(path.join(out.shotsDir, f)).mtime.getTime(), NEUTRAL_FILE_TIME.getTime());
    const runs = { ours: { status: 'verified', screenshots: [{ file: files[0], moment: 'start' }] }, odoo: { status: 'verified', screenshots: [{ file: files[1], moment: 'start' }] } };
    const review = writeReview(dir, [{ cmp: { task: 'find-record' }, runs }]);
    assert.equal(path.dirname(review), out.blindDir, 'review.html sits in blind/');
    assert.deepEqual(fs.readdirSync(out.blindDir).sort(), ['review.html', 'shots']);
    assert.ok(fs.existsSync(path.join(dir, 'key.json')) && !fs.existsSync(path.join(out.blindDir, 'key.json')));
    assert.match(fs.readFileSync(review, 'utf8'), /src="shots\/1111111111111111\.jpg"/);
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
});

test('side by side, the products run in a random order per task', async () => {
  const { productOrder } = await import('../lib/blind.mjs');
  assert.deepEqual(productOrder(['ours', 'odoo'], () => 0.9), ['ours', 'odoo']);
  assert.deepEqual(productOrder(['ours', 'odoo'], () => 0.1), ['odoo', 'ours']);
  const seen = new Set();
  for (let i = 0; i < 200; i++) seen.add(productOrder(['ours', 'odoo']).join());
  assert.equal(seen.size, 2, 'both orders occur');
  assert.deepEqual(productOrder(['odoo']), ['odoo']);
});

test('identity codes are masked wherever they stand in a text: a workspace label, an e-mail domain (round 5 blindness finding)', async () => {
  const { launch, newContext } = await import('../lib/browser.mjs');
  const { maskLocators, identityWordPattern } = await import('../lib/blind.mjs');
  assert.ok(brandingFor('ours').identityWords.includes('alnoor'));
  assert.ok(brandingFor('odoo').identityWords.includes('demo-trading'));
  const re = identityWordPattern(['alnoor']);
  for (const t of ['workspace alnoor', 'admin@alnoor.example', 'مساحة العمل alnoor', 'ALNOOR']) assert.ok(re.test(t), t);
  for (const t of ['alnoorish', 'xalnoor', 'mariam.khoury.000001@staff.example']) assert.ok(!re.test(t), t);
  const browser = await launch();
  try {
    const page = await (await newContext(browser)).newPage();
    await page.setContent(`<header><span id="ws">workspace alnoor</span></header><table><tr><td id="a">admin@alnoor.example</td><td id="b">mariam.khoury.000001@staff.example</td></tr></table>
      <p id="c">مساحة العمل alnoor</p>`);
    const masked = new Set();
    for (const loc of maskLocators(page, brandingFor('ours'))) for (const id of await loc.evaluateAll(els => els.map(e => e.id))) masked.add(id);
    assert.ok(masked.has('ws') && masked.has('a') && masked.has('c'), `masked: ${[...masked].join(', ')}`);
    assert.ok(!masked.has('b'), 'a dataset e-mail is not branding');
  } finally { await browser.close(); }
});
