// Critic p05 r5: shared Playwright helpers.
import { chromium } from '/home/shan/critic/p05-list-search-r5/gauntlet/compare/node_modules/playwright-core/index.mjs';
export const base = 'http://localhost:20550';
export const out = '/home/shan/evidence-staging/p05-list-search/r5/';
export async function open(email, { width = 1440, height = 900 } = {}) {
  const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
  const ctx = await browser.newContext({ viewport: { width, height } });
  await ctx.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: base });
  const page = await ctx.newPage();
  await page.goto(base + '/');
  await page.locator('input[type=email], input[autocomplete=username], input[name=email]').first().fill(email);
  await page.locator('input[type=password]').first().fill('Demo-Pass-2026');
  await page.keyboard.press('Enter');
  await page.waitForLoadState('networkidle');
  return { browser, ctx, page };
}
export const focus = page => page.evaluate(() => { const e = document.activeElement; return e ? `${e.tagName.toLowerCase()}${e.getAttribute('role') ? '[role=' + e.getAttribute('role') + ']' : ''} "${(e.getAttribute('aria-label') || e.textContent || e.value || '').trim().slice(0, 70)}"` : null; });
export const shot = (page, n) => page.screenshot({ path: out + n, type: 'jpeg', quality: 60 });
