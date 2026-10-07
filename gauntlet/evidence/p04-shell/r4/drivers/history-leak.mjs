// One tab: tenant B (gulfsteel) searches its users and opens one, moves on, signs out; tenant A
// (alnoor) signs in in the same tab and presses the browser's Back button. What of B does A's screen show?
import { chromium } from 'playwright-core';
const base = process.argv[2] ?? 'http://localhost:20450';
const out = process.argv[3] ?? '.';
const pw = 'Demo-Pass-2026';
const browser = await chromium.launch();
const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 } });
const page = await ctx.newPage();
async function signIn(email) {
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type(pw); await page.keyboard.press('Enter');
  await page.locator('nav.navpane').waitFor(); await page.waitForTimeout(400);
}
await page.goto(base + '/');
await signIn('admin@gulfsteel.example');
await page.locator('nav.navpane a[href="/identity/users"]').first().click();
await page.locator('main table tbody tr').first().waitFor();
const search = page.locator('main input[type=search]').first();
await search.click();
const secret = 'Khalifa Steel Supplies';
await page.keyboard.type(secret);
await page.waitForTimeout(1200);
console.log('B url after search:', page.url());
await page.locator('nav.navpane a[href="/identity/roles"]').first().click();
await page.locator('main table tbody tr').first().waitFor();
await page.getByRole('button', { name: 'Sign out' }).click();
await page.locator('input[name="email"]:focus').waitFor();
console.log('after sign-out url:', page.url(), 'history length', await page.evaluate(() => history.length));
await signIn('admin@alnoor.example');
console.log('A signed in at', page.url(), await page.evaluate(() => document.querySelector('header')?.innerText.slice(0, 120)));
for (let i = 1; i <= 3; i++) {
  await page.goBack().catch(e => console.log('goBack', e.message));
  await page.waitForTimeout(1500);
  const s = await page.evaluate(() => ({ url: location.href, inputs: [...document.querySelectorAll('input')].map(e => e.value).filter(Boolean), tenant: document.querySelector('header')?.innerText.replace(/\s+/g, ' ').slice(0, 100) }));
  console.log(`A Back ${i}:`, JSON.stringify(s), 'contains B search text:', JSON.stringify(s).includes(secret.split(' ')[0]));
  if (JSON.stringify(s).includes('Khalifa')) await page.screenshot({ path: `${out}/09-tenant-A-back-shows-B-search.jpg`, type: 'jpeg', quality: 60 });
}
await browser.close();
