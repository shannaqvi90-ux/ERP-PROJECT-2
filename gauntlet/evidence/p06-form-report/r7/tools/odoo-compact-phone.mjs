// Critic hand check (p06 r7): does Odoo's shortest edit path accept the number typed without spaces?
// Triple-click the phone field, type +971505550199, click Save; read the stored value back; restore.
import { chromium } from '/home/shan/critic/p06-form-report-r7/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:8069';
async function rpc(path, params, cookie) {
  const r = await fetch(BASE + path, { method: 'POST', headers: { 'Content-Type': 'application/json', ...(cookie ? { Cookie: cookie } : {}) }, body: JSON.stringify({ jsonrpc: '2.0', method: 'call', params }) });
  return { json: await r.json(), cookie: r.headers.get('set-cookie') };
}
const auth = await rpc('/web/session/authenticate', { db: 'reference', login: 'admin', password: 'admin' });
const sid = /session_id=([^;]+)/.exec(auth.cookie)[1];
const cookie = `session_id=${sid}`;
const kw = (model, method, args, kwargs = {}) => rpc(`/web/dataset/call_kw/${model}/${method}`, { model, method, args, kwargs }, cookie).then(r => { if (r.json.error) throw new Error(JSON.stringify(r.json.error)); return r.json.result; });
const [c] = await kw('res.partner', 'search_read', [[['ref', '=', 'C073519']]], { fields: ['id', 'phone', 'country_id'] });
console.log('before', JSON.stringify(c));
const original = c.phone;
const browser = await chromium.launch({ executablePath: process.env.CHROME || '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 } });
await ctx.addCookies([{ name: 'session_id', value: sid, domain: 'localhost', path: '/' }]);
const page = await ctx.newPage();
await page.goto(`${BASE}/odoo/contacts/${c.id}`);
const input = page.locator('.o_form_view .o_field_widget[name="phone"] input');
await input.waitFor();
await page.waitForTimeout(1500);
const t0 = Date.now();
await input.click({ clickCount: 3 });
const sel = await input.evaluate(e => [e.selectionStart, e.selectionEnd, e.value.length]);
console.log('selection after triple-click', JSON.stringify(sel));
const typed = process.argv[2] || '+971505550199';
await page.keyboard.type(typed);
await page.locator('.o_form_view .o_form_button_save').click();
await page.locator('.o_form_view .o_form_button_save').waitFor({ state: 'hidden' });
console.log('machine ms', Date.now() - t0);
const shown = await input.inputValue();
const [after] = await kw('res.partner', 'read', [[c.id]], { fields: ['phone'] });
console.log('typed', JSON.stringify(typed), 'shown', JSON.stringify(shown), 'stored', JSON.stringify(after.phone), 'equals task value', after.phone === '+971 50 555 0199');
await page.screenshot({ path: process.argv[3] || '/home/shan/evidence-staging/p06-form-report/r7/compare-hand/odoo-compact-after-save.png' });
await kw('res.partner', 'write', [[c.id], { phone: original }]);
const [restored] = await kw('res.partner', 'read', [[c.id]], { fields: ['phone'] });
console.log('restored', JSON.stringify(restored.phone));
await browser.close();
