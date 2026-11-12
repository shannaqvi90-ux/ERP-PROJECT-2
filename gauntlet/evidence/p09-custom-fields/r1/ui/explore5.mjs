import { chromium } from '/home/shan/critic/p09-custom-fields-r1/gauntlet/compare/node_modules/playwright-core/index.mjs';
const B = 'http://localhost:20950', OUT = '/home/shan/evidence-staging/p09-custom-fields/r1/ui/';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const log = (...a) => console.log(...a);
await page.goto(B + '/');
await page.locator('input[name="email"]:focus').waitFor();
await page.keyboard.type('admin@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.locator('main h1').waitFor();
await page.goto(B + '/contacts/contacts?filter=' + encodeURIComponent("cfCreditLimit ge 1000") + '&cols=nameEn%2Cemail%2CcfCreditLimit%2CcfCustomerSegment');
await page.locator('table[role=grid] tbody tr').first().waitFor();
log('row', (await page.locator('table[role=grid] tbody tr').first().innerText()).replace(/\s+/g, ' '));
await page.locator('table[role=grid] tbody tr').first().click();
await page.keyboard.press('Enter');
await page.waitForTimeout(1200);
log('form head', (await page.locator('.list-record').innerText()).split('\n').slice(0, 4).join(' / '));
await page.screenshot({ path: OUT + 'en-list-decimals-and-change-line.jpg', type: 'jpeg', quality: 60 });
// history panel
await page.locator('.list-record').getByRole('button', { name: 'History' }).click().catch(e => log('no history btn'));
await page.waitForTimeout(1500);
{ const t = await page.locator('.list-record').innerText(); const i = t.indexOf('Every change'); log('history', t.slice(i, i + 700).replace(/\n/g, ' / ')); }
await page.screenshot({ path: OUT + 'en-record-history.jpg', type: 'jpeg', quality: 60 });
await browser.close();
