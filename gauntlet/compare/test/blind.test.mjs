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
