// Live demo of plant C2 on the planted build: B opens a record from the palette, signs out; A signs
// in in the same tab. What does A's tab hold (and send to the server) of B?
import { chromium } from 'playwright-core';
const base = process.argv[2] ?? 'http://localhost:20470';
const pw = 'Demo-Pass-2026';
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const page = await (await browser.newContext()).newPage();
const sent = [];
page.on('request', r => { const c = r.headers()['cookie']; if (c && /recentRecord/.test(c)) sent.push(`${r.method()} ${new URL(r.url()).pathname}`); });
async function signIn(email) {
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type(pw); await page.keyboard.press('Enter');
  await page.locator('nav.navpane').waitFor(); await page.waitForTimeout(400);
}
await page.goto(base + '/');
await page.evaluate(() => { localStorage.clear(); localStorage.setItem('erp.language', 'en'); });
await page.goto(base + '/');
await signIn('admin@gulfsteel.example');
await page.keyboard.press('Control+k');
await page.keyboard.type('admin');
const opt = page.locator('[role=dialog] [role=option]', { hasText: '@gulfsteel' }).first();
await opt.waitFor();
await opt.click();
await page.waitForTimeout(800);
await page.getByRole('button', { name: 'Sign out', exact: true }).click();
await page.locator('input[name="email"]:focus').waitFor();
await signIn('admin@alnoor.example');
await page.locator('nav.navpane a[href="/identity/users"]').first().click();
await page.locator('main table tbody tr').first().waitFor();
console.log('signed in as A:', await page.evaluate(() => document.querySelector('header')?.innerText.replace(/\s+/g, ' ').slice(0, 60)));
console.log("A's tab document.cookie:", decodeURIComponent(await page.evaluate(() => document.cookie)));
console.log("A's requests that carried B's record to the server:", sent.length, sent.slice(-4));
await browser.close();
