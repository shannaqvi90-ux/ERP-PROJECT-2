import { createRequire } from 'node:module';
const require = createRequire('/home/user/critic/p04-shell-r2/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
const base = 'http://localhost:20450', out = '/home/user/evidence-staging/p04-shell/r2';
const b = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const ctx = await b.newContext({ viewport: { width: 1366, height: 820 } });
const page = await ctx.newPage();
await page.goto(base + '/');
await page.fill('input[name=email]', 'admin@alnoor.example');
await page.fill('input[name=password]', 'Demo-Pass-2026');
await page.keyboard.press('Enter');
await page.locator('nav.navpane').waitFor();
await page.goto(base + '/identity/users');
await page.locator('main table tbody tr').first().waitFor();
await page.waitForTimeout(800);
console.log('layout', JSON.stringify(await page.evaluate(() => {
  const sb = document.querySelector('footer.statusbar'); const r = sb.getBoundingClientRect();
  const rows = [...document.querySelectorAll('main table tbody tr')].slice(0, 8).map(tr => [...tr.children].map(td => Math.round(td.getBoundingClientRect().x)).join(','));
  return { statusTop: r.top, statusBottom: r.bottom, vh: innerHeight, docH: document.documentElement.scrollHeight, rows, trDisplay: getComputedStyle(document.querySelector('main table tbody tr')).display };
})));
// keyboard: focus grid, move to third row, Enter
await page.locator('main table[role=grid]').focus();
for (let i = 0; i < 2; i++) await page.keyboard.press('ArrowDown');
const active = await page.evaluate(() => { const g = document.querySelector('main table[role=grid]'); const id = g.getAttribute('aria-activedescendant'); return document.getElementById(id)?.textContent?.slice(0, 80); });
console.log('active', active);
await page.keyboard.press('Enter');
await page.waitForTimeout(1000);
console.log('after enter', page.url(), JSON.stringify(await page.evaluate(() => ({ dialog: document.querySelector('[role=dialog]')?.getAttribute('aria-label') || document.querySelector('[role=dialog] h2')?.textContent, focus: document.activeElement?.tagName + ' ' + (document.activeElement?.getAttribute('name') || document.activeElement?.getAttribute('aria-label') || '') }))));
await page.screenshot({ path: out + '/09-record-open-en.jpg', type: 'jpeg', quality: 60 });
// Escape returns focus?
await page.keyboard.press('Escape');
await page.waitForTimeout(400);
console.log('after esc', page.url(), await page.evaluate(() => document.activeElement?.tagName + ' ' + document.activeElement?.getAttribute('aria-activedescendant')));
await b.close();
