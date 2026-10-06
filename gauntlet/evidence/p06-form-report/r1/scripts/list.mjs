import { chromium } from 'playwright-core';
const BASE = 'http://localhost:20650', OUT = '/home/shan/evidence-staging/p06-form-report/r1';
const b = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const ctx = await b.newContext({ viewport: { width: 1440, height: 900 } });
const page = await ctx.newPage();
await page.goto(BASE + '/');
await page.locator('input[name="email"]').fill('admin.ar@alnoor.example');
await page.locator('input[name="password"]').fill('Demo-Pass-2026');
await page.keyboard.press('Enter');
await page.locator('nav[aria-label]').first().waitFor();
await page.goto(BASE + '/tenancy/branches');
await page.locator('table[role=grid] tbody tr').first().waitFor();
// keyboard: find print button by tabbing
const btn = page.getByRole('button', { name: 'طباعة أو تصدير' });
console.log('print button aria-keyshortcuts', await btn.getAttribute('aria-keyshortcuts'), 'title', await btn.getAttribute('title'));
await btn.click();
await page.waitForTimeout(300);
console.log('menu', await page.getByRole('menuitem').allTextContents());
console.log('hrefs', await page.getByRole('menuitem').evaluateAll(es => es.map(e => e.getAttribute('href'))));
await page.screenshot({ path: `${OUT}/10-list-print-menu-ar.jpg`, type: 'jpeg', quality: 70 });
// on-screen print preview item?
await b.close();
