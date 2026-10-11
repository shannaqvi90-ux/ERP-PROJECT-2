import { createRequire } from 'node:module';
const require = createRequire('/home/shan/critic/p02-tenancy-r9/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
const [base, pw, email, who] = process.argv.slice(2);
const browser = await chromium.launch({ executablePath: `${process.env.HOME}/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome` });
const page = await (await browser.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
await page.goto(base + '/'); await page.locator('input[name="email"]:focus').waitFor();
await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type(pw); await page.keyboard.press('Enter');
await page.locator('main h1').waitFor();
for (const screen of ['branches', 'access', 'tenant']) {
  await page.goto(base + '/tenancy/' + screen); await page.waitForTimeout(1500);
  const s = await page.evaluate(() => ({ buttons: [...document.querySelectorAll('main button')].map(b => b.textContent.trim()).filter(t => /New|Save|Add/.test(t)), enabledInputs: [...document.querySelectorAll('main .record-form input:enabled, main form input:enabled')].length }));
  await page.keyboard.press('Alt+n'); await page.waitForTimeout(500);
  const form = await page.evaluate(() => !!document.querySelector('.record-form'));
  console.log(who, screen, JSON.stringify(s), 'Alt+N opens form:', form);
}
const r = await page.evaluate(async () => { const x = await fetch('/api/tenancy/branches?take=5', { headers: { 'x-erp-request': '1' } }); return (await x.json()).items.map(b => b.companyId); });
const st = await page.evaluate(async (cid) => { const x = await fetch('/api/tenancy/branches', { method: 'POST', headers: { 'x-erp-request': '1', 'content-type': 'application/json' }, body: JSON.stringify({ companyId: cid, nameEn: 'Critic probe branch', nameAr: 'فرع', isActive: true }) }); return [x.status, (await x.text()).slice(0, 200)]; }, r[0]);
console.log(who, 'POST branch in own company:', JSON.stringify(st));
await browser.close();
