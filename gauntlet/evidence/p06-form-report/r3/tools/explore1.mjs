import { open, B, OUT, shot } from './common.mjs';
const { browser, page, writes } = await open('admin@alnoor.example');
await page.goto(B + '/tenancy/companies');
await page.waitForTimeout(1500);
await page.screenshot({ path: `${OUT}/browser/companies-list.png` });
console.log((await page.locator('main').innerText()).slice(0, 1500));
await browser.close();
