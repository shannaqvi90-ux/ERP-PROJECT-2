import { chromium } from '/home/shan/critic/p05-list-search-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
const base = 'http://localhost:20550';
const out = '/home/shan/evidence-staging/p05-list-search/r4/';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const user = process.argv[2] || 'admin@alnoor.example';
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await ctx.newPage();
const log = (...a) => console.log(...a);
await page.goto(base + '/');
await page.getByLabel('E-mail', { exact: true }).fill(user);
await page.getByLabel('Password', { exact: true }).fill('Demo-Pass-2026');
await page.keyboard.press('Enter');
await page.waitForTimeout(2500);
log('after sign-in url', page.url());
await page.getByRole('navigation').getByRole('link', { name: /^(Users|المستخدمون)$/ }).first().click();
await page.waitForTimeout(2500);
log('users url', page.url());
await page.screenshot({ path: out + 'raw-users-list.png' });
const focused = await page.evaluate(() => document.activeElement?.outerHTML.slice(0, 200));
log('focus on arrival', focused);
log('headers', await page.locator('[role="columnheader"], th').allInnerTexts());
log('status', await page.locator('[role="status"], .list-count, [aria-live]').allInnerTexts());
// Column alignment check: x of each cell per column across the first 15 rows
const align = await page.evaluate(() => {
  const rows = [...document.querySelectorAll('[role="row"]')].slice(0, 16);
  return rows.map(r => [...r.querySelectorAll('[role="gridcell"], [role="columnheader"], td, th')].map(c => Math.round(c.getBoundingClientRect().left)));
});
log('cell x per row', JSON.stringify(align));
await browser.close();
