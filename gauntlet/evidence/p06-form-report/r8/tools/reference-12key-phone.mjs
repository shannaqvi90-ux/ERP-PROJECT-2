// Critic p06 r8: hand check of Odoo's shortest edit-and-save path with the 12-key entry (no +).
// Run from gauntlet/compare (uses its playwright-core and chromium finder). Restores the contact afterwards.
import { launch } from '/home/shan/critic/p06-form-report-r8-demo/gauntlet/compare/lib/browser.mjs';
const URL = 'http://localhost:8069', DB = 'reference', REF = 'C073519', ORIGINAL = '+971 54 427 3034';
const entry = process.argv[2] || '971505550199';
async function rpc(model, method, args, kwargs = {}) {
  const r = await fetch(URL + '/jsonrpc', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ jsonrpc: '2.0', method: 'call', params: { service: 'object', method: 'execute_kw', args: [DB, 2, 'admin', model, method, args, kwargs] } }) });
  const j = await r.json(); if (j.error) throw new Error(JSON.stringify(j.error)); return j.result;
}
const [c] = await rpc('res.partner', 'search_read', [[['ref', '=', REF]]], { fields: ['id', 'phone', 'country_id'] });
console.log('needle', c);
await rpc('res.partner', 'write', [[c.id], { phone: ORIGINAL }]);
const browser = await launch();
const page = await (await browser.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
await page.goto(URL + '/web/login');
await page.fill('input[name=login]', 'admin'); await page.fill('input[name=password]', 'admin');
await page.click('button[type=submit]'); await page.waitForURL(/odoo|web/);
await page.goto(`${URL}/odoo/contacts/${c.id}`);
const input = page.locator('.o_form_view .o_field_widget[name="phone"] input');
await input.waitFor();
await input.click({ clickCount: 3 });
const sel = await input.evaluate(e => [e.selectionStart, e.selectionEnd, e.value.length]);
console.log('triple-click selection', sel);
let keys = 0; for (const ch of entry) { await page.keyboard.type(ch); keys += /[A-Z+!@#$%^&*()_{}|:"<>?~]/.test(ch) ? 2 : 1; }
console.log('typed', JSON.stringify(entry), 'keys', keys);
await page.locator('.o_form_button_save').click();
await page.waitForTimeout(1500);
const shown = await input.inputValue();
const [after] = await rpc('res.partner', 'read', [[c.id]], { fields: ['phone'] });
console.log('shown after save', JSON.stringify(shown), 'stored', JSON.stringify(after.phone));
console.log('digits stored', after.phone.replace(/\D/g, ''), 'credited (digits of +971 50 555 0199 in order)?', after.phone.replace(/\D/g, '') === '971505550199');
await page.screenshot({ path: process.argv[3] || '/tmp/odoo12.png' });
await rpc('res.partner', 'write', [[c.id], { phone: ORIGINAL }]);
const [restored] = await rpc('res.partner', 'read', [[c.id]], { fields: ['phone'] });
console.log('restored', restored.phone);
await browser.close();
