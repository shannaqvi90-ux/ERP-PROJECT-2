import { createRequire } from 'node:module';
const require = createRequire('/home/shan/critic/p02-tenancy-r9/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
const base = 'http://localhost:20250';
const browser = await chromium.launch({ executablePath: `${process.env.HOME}/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome` });
for (const lang of ['en','ar']) {
const page = await (await browser.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
await page.goto(base + '/'); await page.evaluate((l) => { localStorage.clear(); localStorage.setItem('erp.language', l); }, lang); await page.goto(base + '/');
await page.locator('input[name="email"]:focus').waitFor();
await page.keyboard.type('admin@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.locator('main h1').waitFor(); await page.waitForTimeout(1500);
const n = await page.evaluate(async () => (await (await fetch('/api/tenancy/companies?take=50&filter=' + encodeURIComponent('isActive eq true'), { headers: { 'x-erp-request': '1' } })).json()).total);
const header = await page.locator('header').first();
const chips = await header.locator('button').allTextContents();
console.log(lang, 'active companies', n, '| header buttons:', JSON.stringify(chips));
console.log(lang, 'overflow', await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth));
await page.screenshot({ path: `/home/shan/evidence-staging/p02-tenancy/r9/${lang==='en'?'06-top-bar-many-companies-en.jpg':'07-top-bar-many-companies-ar.jpg'}`, type: 'jpeg', quality: 60, clip: { x: 0, y: 0, width: 1400, height: 200 } });
}
await browser.close();
