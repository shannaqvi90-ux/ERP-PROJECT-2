import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { BRANDING, assignLetters, blindName, brandingFor, revealsProduct } from '../lib/blind.mjs';
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
