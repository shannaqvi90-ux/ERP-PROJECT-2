// Critic hand check (p06 r7): where Odoo stores a phone as typed ("505550199"), what do its screens show?
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
const [c] = await kw('res.partner', 'search_read', [[['ref', '=', 'C073519']]], { fields: ['id', 'phone', 'name'] });
const original = c.phone;
await kw('res.partner', 'write', [[c.id], { phone: '505550199' }]);
const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 } });
await ctx.addCookies([{ name: 'session_id', value: sid, domain: 'localhost', path: '/' }]);
const page = await ctx.newPage();
try {
  await page.goto(`${BASE}/odoo/contacts/${c.id}`);
  const input = page.locator('.o_form_view .o_field_widget[name="phone"] input');
  await input.waitFor(); await page.waitForTimeout(1500);
  console.log('form after reload shows', JSON.stringify(await input.inputValue()));
  await page.goto(`${BASE}/odoo/action-base.action_partner_form?view_type=list`);
  await page.locator('.o_searchview_input').waitFor(); await page.waitForTimeout(1000);
  await page.locator('.o_searchview_input').fill(c.name); await page.keyboard.press('Enter');
  await page.waitForTimeout(2000);
  const row = page.locator('.o_data_row', { hasText: c.name }).first();
  console.log('list row shows', JSON.stringify((await row.textContent()).replace(/\s+/g, ' ').trim()));
  const found = await kw('res.partner', 'search_count', [[['phone', 'ilike', '+971 50 555 0199']]]);
  const found2 = await kw('res.partner', 'search_count', [[['phone_sanitized', '=', '+971505550199']]]).catch(e => 'n/a ' + e.message.slice(0, 80));
  console.log('search phone ilike "+971 50 555 0199" finds', found, '; phone_sanitized = +971505550199 finds', found2);
} finally {
  await kw('res.partner', 'write', [[c.id], { phone: original }]);
  const [r] = await kw('res.partner', 'read', [[c.id]], { fields: ['phone'] });
  console.log('restored', JSON.stringify(r.phone));
  await browser.close();
}
