// Contact form: custom fields, edit a 2-decimal value by keyboard, save, read the history; Alt+Shift+F from a field.
import { open, log, shot, focused } from './common.mjs';
const L = process.env.TAG || 'en';
const { browser, page } = await open(L);
await page.goto('http://localhost:20950/contacts/contacts?filter=cfInsuranceCover+ge+5000');
await page.locator('table[role=grid] tbody tr').first().waitFor();
await page.locator('table[role=grid] tbody tr').first().click(); await page.waitForTimeout(1500);
const ins = page.getByLabel(L === 'ar' ? 'التغطية التأمينية' : 'Insurance cover', { exact: true });
log('custom input', await ins.count(), await ins.inputValue().catch(() => '-'));
const inc = page.getByLabel(L === 'ar' ? 'شروط التسليم' : 'Incoterm', { exact: true });
log('incoterm', await inc.count(), JSON.stringify(await inc.evaluate(e => e.tagName === 'SELECT' ? [...e.options].map(o => o.text) : e.value).catch(() => '-')));
const before = await ins.inputValue();
await ins.focus(); await page.keyboard.press('Control+a'); await page.keyboard.type(before.startsWith('1234.5') ? '1234.57' : '1234.56');
log('typed', await ins.inputValue());
await page.keyboard.press('Control+s'); await page.waitForTimeout(2000);
log('status', (await page.locator('[role=status],[role=alert]').allInnerTexts()).join(' | ').slice(0, 200));
log('value after save', await ins.inputValue());
const hist = page.getByRole('button', { name: L === 'ar' ? /السجل|التاريخ|سجل/ : 'History' }).first();
log('history button', await hist.count());
await hist.click().catch(e => log('no history button', e.message.slice(0, 100))); await page.waitForTimeout(2000);
const panel = await page.locator('main, aside').last().innerText();
const i = panel.search(L === 'ar' ? /التغطية التأمينية/ : /Insurance cover/);
log('history text around field', i >= 0 ? panel.slice(Math.max(0, i - 200), i + 200).replace(/\n/g, ' / ') : 'FIELD NOT IN HISTORY');
await shot(page, L + '-contact-history');
// Alt+Shift+F from the custom field
await ins.focus(); await page.keyboard.press('Alt+Shift+F'); await page.waitForTimeout(2500);
log('after Alt+Shift+F url', decodeURIComponent(page.url()));
log('count', await page.locator('main').getByText(/\d+ (contacts|contact)/).first().innerText().catch(() => '-'));
await browser.close();
