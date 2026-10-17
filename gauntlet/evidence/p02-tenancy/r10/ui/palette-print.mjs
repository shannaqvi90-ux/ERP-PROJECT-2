// Critic p02 r10: palette queries for the new print/export actions (full administrator).
import { createRequire } from 'node:module';
const require = createRequire('/home/shan/critic/p02-tenancy-r10/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
const base = process.argv[2];
const browser = await chromium.launch({ executablePath: `${process.env.HOME}/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome` });
const page = await (await browser.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
await page.goto(base + '/'); await page.locator('input[name="email"]:focus').waitFor();
await page.keyboard.type('admin@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.locator('main h1').waitFor(); await page.waitForTimeout(600);
for (const q of ['print', 'export compan', 'companies list', 'excel', 'export branches']) {
  await page.keyboard.press('Control+k'); await page.waitForTimeout(300); await page.keyboard.type(q); await page.waitForTimeout(900);
  console.log(`palette "${q}":`, JSON.stringify((await page.locator('[role=option]').allTextContents()).slice(0, 5)));
  await page.keyboard.press('Escape'); await page.waitForTimeout(200);
}
await page.keyboard.press('Control+k'); await page.waitForTimeout(300); await page.keyboard.type('export compan'); await page.waitForTimeout(900);
const opts = await page.locator('[role=option]').allTextContents();
if (opts.length) { await page.keyboard.press('Enter'); await page.waitForTimeout(1500); console.log('Enter ->', new URL(page.url()).pathname, 'dialog:', JSON.stringify((await page.locator('[role=dialog]').allTextContents()).join(' | ').slice(0, 200))); }
await browser.close();
