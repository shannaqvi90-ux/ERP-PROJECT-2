import { chromium } from 'playwright';
export const B = 'http://localhost:20650';
export const OUT = '/home/shan/evidence-staging/p06-form-report/r3';
export async function open(user, { viewport = { width: 1440, height: 900 } } = {}) {
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport, acceptDownloads: true });
  const page = await context.newPage();
  const writes = [];
  page.on('request', r => { if (!['GET', 'HEAD', 'OPTIONS'].includes(r.method())) writes.push(`${r.method()} ${r.url()}`); });
  page.on('dialog', d => { writes.push(`DIALOG ${d.type()} ${d.message()}`); d.dismiss(); });
  await page.goto(B + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(user);
  await page.keyboard.press('Tab');
  await page.keyboard.type('Demo-Pass-2026');
  await page.keyboard.press('Enter');
  await page.locator('nav').first().waitFor();
  await page.waitForTimeout(500);
  return { browser, context, page, writes };
}
export const shot = (page, name, opts = {}) => page.screenshot({ path: `${OUT}/shots/${name}.jpg`, type: 'jpeg', quality: 70, ...opts });
