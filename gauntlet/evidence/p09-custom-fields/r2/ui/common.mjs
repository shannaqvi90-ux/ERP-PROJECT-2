import { chromium } from '/home/shan/critic/p09-custom-fields-r2/gauntlet/compare/node_modules/playwright-core/index.mjs';
export const B = 'http://localhost:20950', OUT = '/home/shan/evidence-staging/p09-custom-fields/r2/ui/';
export const log = (...a) => console.log(...a);
export async function open(L = 'en', who = 'admin@alnoor.example') {
  const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, acceptDownloads: true });
  const page = await ctx.newPage();
  page.on('pageerror', e => log('PAGEERROR', e.message));
  page.on('response', r => { if (r.status() >= 400) log('HTTP', r.status(), r.request().method(), r.url().slice(0, 220)); });
  await page.goto(B + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(who); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.locator('main h1').waitFor();
  await page.waitForTimeout(800);
  const lang = await page.evaluate(() => document.documentElement.lang);
  if (L === 'ar' && lang !== 'ar') { await page.getByRole('button', { name: 'العربية' }).click(); await page.waitForTimeout(1500); }
  if (L === 'en' && lang === 'ar') { await page.getByRole('button', { name: 'English' }).click(); await page.waitForTimeout(1500); }
  return { browser, page };
}
export const focused = (page) => page.evaluate(() => { const e = document.activeElement; const lab = e?.labels?.[0]?.innerText || e?.getAttribute('aria-label') || e?.innerText?.slice(0, 40) || ''; return `${e?.tagName}:${e?.type || ''}:${lab.replace(/\n/g, ' ')}`; });
export const shot = (page, name) => page.screenshot({ path: OUT + name + '.jpg', type: 'jpeg', quality: 55 });
