import { open, shot, BASE } from './common.mjs';
const { browser, page } = await open('admin.ar@alnoor.example');
await page.keyboard.press('g'); await page.keyboard.press('j');
await page.waitForFunction(() => document.querySelectorAll('table[role=grid] tbody tr').length > 0);
console.log('focus', await page.evaluate(() => document.activeElement.className));
await page.keyboard.type('فحص ذاتي لطابور المهام', { delay: 30 });
await page.waitForTimeout(2500);
console.log('search value', await page.locator('input.search').inputValue(), 'rows', await page.evaluate(() => document.querySelectorAll('table[role=grid] tbody tr').length), 'first', await page.evaluate(() => document.querySelector('table[role=grid] tbody tr')?.textContent.slice(0, 90)), 'count', await page.evaluate(() => document.querySelector('main').innerText.slice(0, 200)));
await page.keyboard.press('Enter');
await page.waitForTimeout(2500);
console.log('after Enter url', page.url(), 'record?', await page.locator('.list-record').count());
if (await page.locator('.list-record').count()) { console.log((await page.locator('.list-record').innerText()).slice(0, 600)); await shot(page, '09-job-panel-ar'); }
await browser.close();
