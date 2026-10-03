// CLAUDE.md rule 6: dependencies under MIT, Apache-2.0, BSD or the PostgreSQL licence only.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { HARNESS_DIR } from '../lib/config.mjs';

const ALLOWED = /^(MIT|Apache-2\.0|BSD-2-Clause|BSD-3-Clause|0BSD|PostgreSQL)$/;

test('every npm dependency of the harness is under an allowed licence', () => {
  const lock = JSON.parse(fs.readFileSync(path.join(HARNESS_DIR, 'package-lock.json'), 'utf8'));
  const pkgs = Object.entries(lock.packages).filter(([k]) => k);
  assert.ok(pkgs.length >= 1);
  for (const [name, p] of pkgs) {
    const lic = p.license || JSON.parse(fs.readFileSync(path.join(HARNESS_DIR, name, 'package.json'), 'utf8')).license;
    assert.match(String(lic), ALLOWED, `${name}: ${lic}`);
  }
});

test('dependencies are pinned to exact versions', () => {
  const pkg = JSON.parse(fs.readFileSync(path.join(HARNESS_DIR, 'package.json'), 'utf8'));
  for (const [n, v] of Object.entries({ ...pkg.dependencies, ...pkg.devDependencies })) assert.match(v, /^\d+\.\d+\.\d+$/, `${n}@${v}`);
});
