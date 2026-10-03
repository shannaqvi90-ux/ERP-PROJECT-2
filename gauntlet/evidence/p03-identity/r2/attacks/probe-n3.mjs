import { chromium } from 'playwright-core';
const B = 'http://localhost:20350';
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
await page.goto(B + '/');
await page.locator('input[name="email"]').fill('admin@alnoor.example');
await page.locator('input[name="password"]').fill('Demo-Pass-2026');
await page.keyboard.press('Enter');
await page.locator('nav[aria-label="Main navigation"]').waitFor();
for (const [path, input] of [['/identity/users', 'input[name="email"]'], ['/identity/roles', 'input[name="nameEn"]']]) {
  await page.locator(`nav a[href="${path}"]`).first().click();
  await page.locator('main table tbody tr').first().waitFor();
  await page.waitForTimeout(400);
  const focus = await page.evaluate(() => document.activeElement?.getAttribute('aria-label') || document.activeElement?.tagName);
  await page.keyboard.press('n');
  await page.waitForTimeout(600);
  console.log(`${path}: focus on arrival=${focus}; after pressing n the new-record form is open: ${await page.locator(input).count() > 0 && (await page.locator(input).inputValue()) === ''}; search box now holds: "${await page.locator('main input[type=search], main .search').first().inputValue().catch(()=>'?')}"`);
  await page.keyboard.press('Escape');
}
await browser.close();
