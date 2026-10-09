// Critic p04 r7: Arabic PDF of the users list by keyboard; nav pane state after narrow window.
const { chromium } = require('/home/shan/critic/p04-shell-r7/gauntlet/compare/node_modules/playwright-core');
const fs = require('fs');
const BASE = 'http://localhost:20450', OUT = '/home/shan/evidence-staging/p04-shell/r7', PW = 'Demo-Pass-2026';
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
(async () => {
  const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
  const ctx = await browser.newContext({ locale: 'en-US', viewport: { width: 1366, height: 768 }, timezoneId: 'Asia/Dubai', acceptDownloads: true });
  const page = await ctx.newPage();
  await page.goto(BASE + '/'); await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type('admin.ar@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type(PW); await page.keyboard.press('Enter');
  await page.locator('main').waitFor(); await page.waitForLoadState('networkidle'); await sleep(500);
  console.log('dir', await page.evaluate(() => document.documentElement.dir), 'navHidden', await page.evaluate(() => document.querySelector('nav.navpane')?.hidden));
  await page.goto(BASE + '/identity/users'); await page.locator('main table tbody tr').first().waitFor(); await sleep(500);
  await page.keyboard.press('Alt+Shift+r'); await sleep(700);
  const dl = page.waitForEvent('download', { timeout: 60000 });
  await page.keyboard.press('Enter');
  const d = await dl; await d.saveAs(OUT + '/users-ar.pdf'); console.log('pdf', d.suggestedFilename());
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
