// Critic p06 r6: is there an Odoo path for edit-and-save with fewer keys than the harness's Ctrl+A path?
// Triple-click the phone field (the browser selects the whole value), type the number, click Save.
import { chromium } from '/home/shan/critic/p06-form-report-r6/gauntlet/compare/node_modules/playwright-core/index.mjs';
const base = 'http://localhost:8069';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await (await browser.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
await page.goto(base + '/web/login?db=reference');
await page.fill('input[name=login]', 'admin'); await page.fill('input[name=password]', 'admin'); await page.keyboard.press('Enter');
await page.waitForURL(/\/odoo/);
const rpc = async (model, method, args, kwargs = {}) => (await (await page.request.post(base + '/web/dataset/call_kw', { data: { jsonrpc: '2.0', method: 'call', params: { model, method, args, kwargs } } })).json()).result;
const [c] = await rpc('res.partner', 'search_read', [[['ref', '=', 'C073519']]], { fields: ['id', 'phone'] });
console.log('contact', c);
const original = c.phone;
await page.goto(`${base}/odoo/contacts/${c.id}`);
const phone = page.locator('.o_form_view .o_field_widget[name="phone"] input');
await phone.waitFor(); await page.waitForTimeout(1500);
await phone.click({ clickCount: 3 });
const sel = await phone.evaluate(el => [el.selectionStart, el.selectionEnd, el.value.length]);
console.log('after triple-click selection', sel);
await page.keyboard.type('+971 50 555 0199');
await page.locator('.o_form_view .o_form_button_save').click();
await page.locator('.o_form_view .o_form_button_save').waitFor({ state: 'hidden' });
const [after] = await rpc('res.partner', 'read', [[c.id]], { fields: ['phone'] });
console.log('saved phone', after.phone, 'shown', await phone.inputValue());
await rpc('res.partner', 'write', [[c.id], { phone: original }]);
const [restored] = await rpc('res.partner', 'read', [[c.id]], { fields: ['phone'] });
console.log('restored', restored.phone);
await browser.close();
