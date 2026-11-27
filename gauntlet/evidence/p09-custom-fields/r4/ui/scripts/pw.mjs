import pc from '/home/shan/critic/p09-custom-fields-r4-demo/gauntlet/compare/node_modules/playwright-core/index.js';
const { chromium } = pc;
export const BASE = process.env.BASE || 'http://localhost:20950';
export const OUT = '/home/shan/evidence-staging/p09-custom-fields/r4/ui';
const EXE = '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
export async function open(email, opts = {}) {
  const browser = await chromium.launch({ executablePath: EXE });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, acceptDownloads: true, ...opts });
  const page = await ctx.newPage();
  const errors = [];
  page.on('response', r => { if (r.status() >= 500) errors.push(r.status() + ' ' + r.url()); });
  page.on('pageerror', e => errors.push('pageerror ' + e.message));
  await page.goto(BASE + '/');
  for (let i = 0; i < 20; i++) {
    await page.locator('input[type=email], input[autocomplete=username], input[name=email]').first().fill(email);
    await page.keyboard.press('Enter');
    await page.locator('input[type=password]').first().fill('Demo-Pass-2026');
    await page.keyboard.press('Enter');
    try { await page.waitForSelector('nav, [role=navigation]', { timeout: 15000 }); break; } catch { await page.waitForTimeout(3000); await page.goto(BASE + '/'); }
  }
  await page.waitForTimeout(1500);
  return { browser, ctx, page, errors };
}
export const shot = (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 60 });
export const focused = page => page.evaluate(() => { const e = document.activeElement; return e ? `${e.tagName} name=${e.getAttribute('name')} label=${e.getAttribute('aria-label') ?? (e.labels?.[0]?.textContent ?? '')} value=${e.value ?? ''}` : 'none'; });
export const text = page => page.evaluate(() => document.body.innerText);
