import { chromium } from '/home/shan/critic/p06-form-report-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
import { appendFileSync } from 'node:fs';
const base = process.argv[2]; const shots = '/home/shan/evidence-staging/p06-form-report/r4';
const L = (...a) => { const s = a.join(' '); console.log(s); appendFileSync(shots + '/browser/explore.log', '\n' + s); };
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
async function session(email) {
  const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 }, acceptDownloads: true });
  const page = await ctx.newPage();
  await page.goto(base + '/');
  await page.locator('input[name="email"]').fill(email);
  await page.locator('input[name="password"]').fill('Demo-Pass-2026');
  await page.keyboard.press('Enter');
  await page.locator('nav[aria-label]').first().waitFor();
  return { ctx, page };
}
{
  const { ctx, page } = await session('admin@alnoor.example');
  await page.goto(base + '/reports/catalog?report=identity.roleSummary&run=1');
  await page.waitForTimeout(2500);
  L('en role summary on screen:', (await page.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 500));
  await page.screenshot({ path: shots + '/08-report-role-summary-en.jpg', type: 'jpeg', quality: 70 });
  await page.goto(base + '/reports/catalog?report=identity.usersByRole&run=1');
  await page.waitForTimeout(3000);
  L('en users by role on screen:', (await page.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 400));
  await ctx.close();
}
{
  const { ctx, page } = await session('admin.ar@alnoor.example');
  await page.goto(base + '/identity/roles');
  await page.waitForTimeout(2000);
  await page.locator('[role=grid], table').first().click({ position: { x: 20, y: 60 } }).catch(() => {});
  await page.keyboard.press('Alt+Shift+R'); await page.waitForTimeout(500);
  L('ar roles list Alt+Shift+R menu:', JSON.stringify(await page.locator('[role=menu] [role=menuitem]').allTextContents()), 'focus', await page.evaluate(() => document.activeElement?.textContent?.slice(0, 40)));
  await page.screenshot({ path: shots + '/09-list-print-menu-ar.jpg', type: 'jpeg', quality: 70 });
  await page.keyboard.press('Escape');
  await page.goto(base + '/reports/catalog?report=tenancy.branchDirectory&run=1');
  await page.waitForTimeout(2500);
  L('ar branch directory on screen:', (await page.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 400));
  await page.screenshot({ path: shots + '/10-report-branch-directory-ar.jpg', type: 'jpeg', quality: 70 });
  await ctx.close();
}
await browser.close();
