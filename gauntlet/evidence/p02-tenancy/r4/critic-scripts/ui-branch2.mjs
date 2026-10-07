// Critic p02 r4: the branch form for a one-branch administrator: is the code offered for change (the server refuses it)?
import { chromium } from '/home/shan/critic/p02-tenancy-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20250';
const browser = await chromium.launch({ executablePath: process.env.CHROME });
const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
await page.goto(BASE + '/'); await page.locator('input[name="email"]:focus').waitFor();
await page.keyboard.type(process.env.EMAIL); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.locator('nav[aria-label]').first().waitFor(); await page.waitForTimeout(800);
await page.goto(BASE + '/tenancy/branches'); await page.locator('table[role=grid] tbody tr').first().waitFor();
await page.locator('table[role=grid] tbody tr').first().click(); await page.locator('.record-form').waitFor(); await page.waitForTimeout(800);
const code = page.locator('.record-form [data-field="code"] input');
console.log('code field editable:', await code.isEditable());
await code.fill('AQZ-NEW'); await page.keyboard.press('Control+s'); await page.waitForTimeout(1200);
console.log('after save:', (await page.locator('.record-form [role=alert], .record-form .alert').allInnerTexts()).join(' | '));
await browser.close();
