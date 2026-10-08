import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { COUNTS, DEFAULT_OUT, generate, rng } from '../data/generate.mjs';

test('the generator is deterministic: a fresh run reproduces the shared files byte for byte', () => {
  const shared = generate({ out: DEFAULT_OUT });
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-data-'));
  try {
    const again = generate({ out: tmp, force: true });
    assert.deepEqual(again.files, shared.files);
  } finally { fs.rmSync(tmp, { recursive: true, force: true }); }
});

test('volumes match the bar: 100,000 contacts, users and rates; 5,000 import rows', () => {
  generate({ out: DEFAULT_OUT });
  const lines = f => fs.readFileSync(path.join(DEFAULT_OUT, f), 'utf8').trimEnd().split('\n').length - 1;
  assert.equal(COUNTS.contacts, 100_000);
  assert.equal(lines('contacts.csv'), 100_000);
  assert.equal(lines('users.csv'), 100_000);
  assert.equal(lines('rates.csv'), 100_000);
  assert.equal(lines('contacts-import-5000.csv'), 5_000);
});

test('the needle name occurs exactly once and the import file has plain headers', () => {
  const needles = JSON.parse(fs.readFileSync(path.join(DEFAULT_OUT, 'needles.json'), 'utf8'));
  const contacts = fs.readFileSync(path.join(DEFAULT_OUT, 'contacts.csv'), 'utf8');
  assert.equal(contacts.split(`,${needles.contact.name},`).length - 1, 1);
  const header = fs.readFileSync(path.join(DEFAULT_OUT, 'contacts-import-5000.csv'), 'utf8').split('\n')[0];
  assert.equal(header, 'Name,Email,Phone,Street,City,Country');
});

test('the generator keeps rates as fixed six-decimal strings', () => {
  const rows = fs.readFileSync(path.join(DEFAULT_OUT, 'rates.csv'), 'utf8').trimEnd().split('\n').slice(1, 2000);
  for (const r of rows) assert.match(r.split(',')[2], /^\d+\.\d{6}$/);
});

test('the random source is seeded', () => {
  const a = rng(7), b = rng(7);
  for (let i = 0; i < 10; i++) assert.equal(a.next(), b.next());
});
