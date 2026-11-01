import { chromium } from '/home/shan/critic/p12-jobs-r2/gauntlet/compare/node_modules/playwright-core/index.mjs';
export const BASE = 'http://localhost:21250';
export const OUT = '/home/shan/evidence-staging/p12-jobs/r2';
export async function open(email, { width = 1440, height = 900 } = {}) {
  const browser = await chromium.launch({ executablePath: process.env.CHROME || undefined });
  const context = await browser.newContext({ viewport: { width, height } });
  const page = await context.newPage();
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email);
  await page.keyboard.press('Tab');
  await page.keyboard.type('Demo-Pass-2026');
  await page.keyboard.press('Enter');
  await page.locator('nav[aria-label], nav').first().waitFor();
  await page.locator('main h1').waitFor();
  return { browser, context, page };
}
export async function shot(page, name) { await page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 60 }); }
