// r6 critic: every main form has its history (Alt+Y) by keyboard. node forms.mjs <en|ar>
import { chromium } from '/home/shan/critic/p07-audit-r6/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20750', lang = process.argv[2] || 'en';
const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
const errors = []; page.on('response', r => { if (r.status() >= 500) errors.push(`${r.status()} ${r.url().slice(0, 160)}`); }); page.on('pageerror', e => errors.push('pageerror ' + e.message));
await page.goto(BASE + '/');
await page.locator('input[name="email"]').fill(lang === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example'); await page.locator('input[name="password"]').fill('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.waitForSelector('nav'); await page.waitForTimeout(800);
for (const key of ['u', 'r', 'c', 'b', 'a', 'y', 'x', 'n', 'j', 's', 'o', 'w']) {
  await page.keyboard.press('Escape'); await page.locator('body').focus().catch(() => {}); await page.evaluate(() => document.activeElement?.blur());
  await page.keyboard.press('g'); await page.keyboard.press(key); await page.waitForTimeout(1500);
  const url0 = page.url();
  const grid = await page.locator('[role="grid"] [role="row"]').count();
  if (grid > 1) { await page.keyboard.press('ArrowDown'); await page.waitForTimeout(300); await page.keyboard.press('Enter'); await page.waitForTimeout(1800); }
  const latest = await page.locator('[data-testid="audit-latest"]').first().innerText({ timeout: 3000 }).catch(() => '(no latest-change line)');
  await page.keyboard.press('Alt+y'); await page.waitForTimeout(1500);
  const hist = await page.evaluate(() => { const h = [...document.querySelectorAll('h3')].find(e => /History|السجل/.test(e.innerText)); const sec = h?.closest('section,aside,div'); return h ? (sec?.parentElement?.innerText || '').replace(/\s+/g, ' ').slice(0, 220) : '(no history panel)'; });
  console.log(`G ${key.toUpperCase()} | ${url0.replace(BASE, '')} -> ${page.url().replace(BASE, '')} | ${latest.replace(/\s+/g, ' ').slice(0, 110)} | ${hist}`);
  await page.keyboard.press('Escape');
}
console.log('errors', JSON.stringify(errors));
await browser.close();
