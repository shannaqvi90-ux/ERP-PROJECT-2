// Critic p04 r7: palette actions, keyboard reach of actions, mobile Arabic, print layout.
const { chromium } = require('/home/shan/critic/p04-shell-r7/gauntlet/compare/node_modules/playwright-core');
const fs = require('fs');
const BASE = 'http://localhost:20450', OUT = '/home/shan/evidence-staging/p04-shell/r7', PW = 'Demo-Pass-2026';
const log = {}; const note = (k, v) => { log[k] = v; console.log(k, JSON.stringify(v)); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
(async () => {
  const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
  const ctx = await browser.newContext({ locale: 'en-US', viewport: { width: 1366, height: 768 }, timezoneId: 'Asia/Dubai' });
  const page = await ctx.newPage();
  await page.goto(BASE + '/'); await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type('admin@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type(PW); await page.keyboard.press('Enter');
  await page.locator('nav.navpane').waitFor(); await page.waitForLoadState('networkidle');
  const pal = async (q) => { await page.keyboard.press('Control+k'); await page.locator('[role=dialog] input[role=combobox]').waitFor(); await page.keyboard.type(q); await sleep(900);
    const r = await page.evaluate(() => [...document.querySelectorAll('[role=dialog] [role=option]')].map((o) => o.textContent.trim().replace(/\s+/g, ' ').slice(0, 60))); await page.keyboard.press('Escape'); await sleep(150); return r; };
  // Empty palette: all actions.
  await page.keyboard.press('Control+k'); await sleep(700);
  note('palette.empty', await page.evaluate(() => [...document.querySelectorAll('[role=dialog] [role=group], [role=dialog] [role=option]')].map((o) => (o.getAttribute('role') === 'group' ? '## ' + (o.getAttribute('aria-label') || '') : o.textContent.trim().replace(/\s+/g, ' ').slice(0, 60)))));
  await page.keyboard.press('Escape');
  for (const q of [] || ['new', 'new user', 'invite', 'new role', 'new company', 'new branch', 'preferences', 'switch company', 'work in', 'home', 'navigation', 'export', 'copy role', 'unblock', 'sign-in history']) note('palette:' + q, await pal(q));
  // On the users screen.
  await page.goto(BASE + '/identity/users'); await page.locator('main table tbody tr').first().waitFor(); await sleep(400);
  for (const q of []) note('palette@users:' + q, await pal(q));
  // Alt+N from the users screen opens New user?
  await page.keyboard.press('Alt+n'); await sleep(800);
  note('altN@users', await page.evaluate(() => ({ path: location.pathname + location.search, dialog: (document.querySelector('[role=dialog], aside')?.innerText || '').slice(0, 200), focus: document.activeElement?.tagName + ' ' + (document.activeElement?.getAttribute('name') || '') })));
  await page.keyboard.press('Escape'); await sleep(300);
  // Mobile Arabic.
  if (await page.evaluate(() => document.documentElement.dir) !== 'rtl') { await page.keyboard.press('Alt+l'); await page.waitForFunction(() => document.documentElement.dir === 'rtl'); }
  await page.setViewportSize({ width: 390, height: 800 }); await page.goto(BASE + '/identity/users'); await page.locator('main').waitFor(); await sleep(1200);
  note('mobile.ar', await page.evaluate(() => ({ scrollW: document.documentElement.scrollWidth, clientW: document.documentElement.clientWidth })));
  await page.screenshot({ path: OUT + '/09-users-ar-390.jpg', type: 'jpeg', quality: 60 });
  await page.setViewportSize({ width: 1366, height: 768 });
  // Print of the users list in Arabic via Alt+Shift+R.
  await page.goto(BASE + '/identity/users'); await page.locator('main table tbody tr').first().waitFor(); await sleep(500);
  await page.keyboard.press('Alt+Shift+r'); await sleep(900);
  note('print-dialog.ar', await page.evaluate(() => (document.querySelector('[role=dialog]')?.innerText || 'none').slice(0, 600)));
  await page.screenshot({ path: OUT + '/10-print-dialog-ar.jpg', type: 'jpeg', quality: 60 });
  await page.keyboard.press('Escape');
  // Pdf of browser print in Arabic.
  await page.pdf({ path: OUT + '/print-users-ar-browser.pdf', format: 'A4' });
  await page.goto(BASE + '/'); await page.locator('nav.navpane').waitFor(); await page.keyboard.press('Alt+l'); await page.waitForFunction(() => document.documentElement.dir === 'ltr');
  fs.writeFileSync(OUT + '/explore3.json', JSON.stringify(log, null, 1)); await browser.close();
})().catch((e) => { console.error(e); fs.writeFileSync(OUT + '/explore3.json', JSON.stringify({ ...log, fatal: String(e) }, null, 1)); process.exit(1); });
