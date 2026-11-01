import { open, shot } from './common.mjs';
const { browser, page } = await open('admin.ar@alnoor.example');
for (const k of ['c', 'y', 'j']) {
  await page.keyboard.press('g'); await page.keyboard.press(k);
  await page.waitForFunction(() => document.querySelectorAll('table[role=grid] tbody tr').length > 0);
  await page.waitForTimeout(800);
  console.log(k, page.url(), await page.evaluate(() => [...document.querySelectorAll('table[role=grid] tbody tr')].slice(0, 2).map(r => r.textContent.slice(0, 90))));
  await page.keyboard.press('Escape');
}
await page.locator('input.search').focus();
await page.keyboard.type('فحص ذاتي');
await page.waitForTimeout(2000);
console.log('search value', await page.locator('input.search').inputValue(), 'rows', await page.evaluate(() => document.querySelectorAll('table[role=grid] tbody tr').length), await page.evaluate(() => document.querySelector('main').innerText.match(/[\d,٠-٩٬]+ مه[^\n]*/)?.[0]));
await page.keyboard.press('Enter');
await page.waitForTimeout(2500);
console.log('after Enter url', page.url(), 'record?', await page.locator('.list-record').count());
await browser.close();
