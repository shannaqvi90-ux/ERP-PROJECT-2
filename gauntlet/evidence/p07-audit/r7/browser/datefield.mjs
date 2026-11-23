// r7 critic: what the custom date field (Credit review date) accepts when typed by keyboard.
import { chromium } from '/home/shan/critic/p07-audit-r7/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE='http://localhost:20750';
const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
await page.goto(BASE + '/');
await page.locator('input[name="email"]').fill('admin@alnoor.example');
await page.locator('input[name="password"]').fill('Demo-Pass-2026');
await page.keyboard.press('Enter');
await page.waitForSelector('nav'); await page.waitForTimeout(800);
await page.keyboard.press('Control+k'); await page.keyboard.type('Shamma Waleed Al Romaithi'); await page.waitForTimeout(1500); await page.keyboard.press('Enter');
await page.waitForSelector('[data-testid="audit-latest"]', { timeout: 20000 });
const f = page.getByLabel('Credit review date', { exact: true });
console.log('field html', (await f.evaluate(e => e.outerHTML)).slice(0, 400));
console.log('placeholder', await f.getAttribute('placeholder'), 'value', await f.inputValue());
for (const v of ['2026-12-31', '31/12/2026', '31122026']) {
  await f.focus(); await page.keyboard.press('Control+a'); await page.keyboard.press('Backspace');
  await page.keyboard.type(v); await page.keyboard.press('Tab'); await page.waitForTimeout(300);
  console.log('typed', v, '-> value', JSON.stringify(await f.inputValue()), 'invalid', await f.getAttribute('aria-invalid'));
}
await browser.close();
