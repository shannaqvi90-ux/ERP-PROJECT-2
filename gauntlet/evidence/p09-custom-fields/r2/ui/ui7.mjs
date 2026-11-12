// Alt+Shift+F from a custom field in the record form, in the given language, without the history panel.
import { open, log, shot } from './common.mjs';
const L = process.env.TAG || 'en';
const { browser, page } = await open(L);
await page.goto('http://localhost:20950/contacts/contacts?filter=cfInsuranceCover+ge+5000');
await page.locator('table[role=grid] tbody tr').first().waitFor();
await page.locator('table[role=grid] tbody tr').first().click(); await page.waitForTimeout(2000);
const ins = page.getByLabel(L === 'ar' ? 'التغطية التأمينية' : 'Insurance cover', { exact: true });
log('value', await ins.inputValue());
await ins.focus(); await page.keyboard.press('Alt+Shift+F'); await page.waitForTimeout(3000);
log('url', decodeURIComponent(page.url()));
log('count', await page.locator('main').getByText(/\d+ (contacts|contact)|جهة اتصال|جهات اتصال/).first().innerText().catch(() => '-'));
await shot(page, L + '-alt-shift-f');
await browser.close();
