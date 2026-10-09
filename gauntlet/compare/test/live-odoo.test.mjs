// Checks against the running Odoo reference rig. Skipped when the rig is not reachable, so the
// unit suite runs anywhere; `npm run test:live` sets COMPARE_LIVE=1 and then a missing rig fails.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { PRODUCTS } from '../lib/config.mjs';
import { OdooRpc } from '../lib/odoo-rpc.mjs';
import { checkRigVolume, describeShort, TOP_UP_HINT } from '../lib/rig-volume.mjs';
import { loadTasks } from '../lib/registry.mjs';
import { runTask } from '../lib/runner.mjs';

const live = process.env.COMPARE_LIVE === '1';
async function reachable() {
  try { return (await fetch(PRODUCTS.odoo.baseUrl + '/web/login', { signal: AbortSignal.timeout(3000) })).ok; } catch { return false; }
}
const up = await reachable();
if (live && !up) throw new Error(`COMPARE_LIVE=1 but the Odoo rig does not answer on ${PRODUCTS.odoo.baseUrl}; run tools/odoo-reference/up.sh`);

test('the rig holds at least 100,000 rows in every main list right now', { skip: !up && 'Odoo rig not reachable' }, async () => {
  const rpc = await new OdooRpc(PRODUCTS.odoo).login(PRODUCTS.odoo.users.admin);
  const result = await checkRigVolume(rpc);
  assert.equal(Object.keys(result.lists).length, 7, 'every main list is checked');
  assert.ok(result.ok, `${describeShort(result)}; ${TOP_UP_HINT}`);
});

test('the rig serves Arabic and the apps the tasks need', { skip: !up && 'Odoo rig not reachable' }, async () => {
  const rpc = await new OdooRpc(PRODUCTS.odoo).login(PRODUCTS.odoo.users.admin);
  const langs = (await rpc.searchRead('res.lang', [], ['code'])).map(l => l.code);
  assert.ok(langs.includes('ar_001'), langs.join(','));
  const mods = (await rpc.searchRead('ir.module.module', [['state', '=', 'installed'], ['name', 'in', ['contacts', 'purchase', 'base_import', 'mail']]], ['name'])).map(m => m.name);
  assert.equal(mods.length, 4, mods.join(','));
});

for (const t of await loadTasks()) {
  test(`Odoo driver for ${t.id} completes and verifies on the rig`, { skip: (!live && 'set COMPARE_LIVE=1 to drive the rig') || (!up && 'rig down'), timeout: 900_000 }, async () => {
    const out = fs.mkdtempSync(path.join(os.tmpdir(), `compare-live-${t.id}-`));
    try {
      const r = await runTask(t.id, 'odoo', { outDir: out });
      assert.equal(r.status, 'verified', `${t.id}: ${r.status} ${r.error || JSON.stringify(r.verification)}`);
      assert.ok(r.counts.steps > 0 && r.counts.machine_seconds > 0);
      assert.equal(r.cleanup_error, undefined);
    } finally { fs.rmSync(out, { recursive: true, force: true }); }
  });
}
