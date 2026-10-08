import { chromium } from 'playwright-core';
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
for (const [label, B] of [['planted UI (U1)', 'http://localhost:20352'], ['shipped UI', 'http://localhost:20350']]) {
  const page = await browser.newPage();
  await page.goto(B + '/'); await page.locator('input[name="email"]').fill('viewer@alnoor.example'); await page.locator('input[name="password"]').fill('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.locator('nav a[href="/identity/roles"]').first().click(); await page.locator('main table tbody tr').first().waitFor();
  const n = await page.getByRole('button', { name: 'New role' }).count();
  let status = '';
  if (n) { await page.getByRole('button', { name: 'New role' }).click(); await page.locator('input[name="nameEn"]').waitFor(); status = ` -> opens a new-role form (name field disabled: ${await page.locator('input[name="nameEn"]').isDisabled()})`; }
  console.log(`${label}: read-only user (no identity.roles.create) sees "New role": ${n}${status}`);
  await page.close();
}
await browser.close();
