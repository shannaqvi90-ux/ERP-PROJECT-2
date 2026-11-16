import { chromium } from '/home/shan/critic/p11-attachments-r3-plant/gauntlet/compare/node_modules/playwright-core/index.mjs';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
await page.goto('http://localhost:21150/'); await page.evaluate(() => { localStorage.clear(); localStorage.setItem('erp.language', 'en'); }); await page.goto('http://localhost:21150/');
await page.locator('input[name="email"]').waitFor(); await page.keyboard.type('admin@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.waitForTimeout(2500);
await page.goto('http://localhost:21150/contacts/contacts/0188758b-3388-762c-ac40-a20f4083549e'); await page.waitForTimeout(3000);
console.log((await page.locator('[data-testid="attachments"]').evaluate(e => e.outerHTML)).slice(0, 2500));
await browser.close();
