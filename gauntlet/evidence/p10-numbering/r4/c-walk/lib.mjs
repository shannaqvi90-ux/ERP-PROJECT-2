import { chromium } from 'playwright';
export const BASE = 'http://localhost:21050';
export const OUT = '/home/shan/evidence-staging/p10-numbering/r4';
export async function open(email, opts = {}) {
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: opts.locale || 'en-GB' });
  const page = await ctx.newPage();
  const errors = [];
  page.on('response', r => { if (r.status() >= 500) errors.push(`${r.status()} ${r.request().method()} ${r.url()}`); });
  page.on('pageerror', e => errors.push('pageerror ' + e.message));
  await page.goto(BASE + '/');
  await page.waitForLoadState('networkidle');
  const emailBox = page.locator('input[type=email], input[name=email], input[autocomplete=username]').first();
  await emailBox.waitFor();
  await emailBox.fill(email);
  await page.keyboard.press('Enter');
  const pw = page.locator('input[type=password]').first();
  await pw.waitFor();
  await pw.fill('Demo-Pass-2026');
  await page.keyboard.press('Enter');
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(1500);
  return { browser, ctx, page, errors };
}
export async function shot(page, name) { await page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 60 }); }
