import { chromium } from '/home/shan/critic/p11-attachments-r3-plant/gauntlet/compare/node_modules/playwright-core/index.mjs';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
await page.goto('http://localhost:21150/'); await page.evaluate(() => { localStorage.clear(); localStorage.setItem('erp.language', process.argv[3]||'en'); }); await page.goto('http://localhost:21150/');
await page.locator('input[name="email"]').waitFor(); await page.keyboard.type('admin@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.waitForTimeout(2500);
await page.goto('http://localhost:21150/contacts/contacts/0188c658-ac00-7a7d-a6d2-218a1566bc03'); await page.waitForTimeout(3000);
const add = page.locator('[data-testid="attachments"] .attachments-head button');
await page.locator('h2').first().click();
for (const how of (process.argv[2]||'enter,space,click').split(',')) {
  await add.focus();
  const t0 = Date.now();
  try {
    const p = page.waitForEvent('filechooser', { timeout: 6000 });
    if (how === 'enter') await page.keyboard.press('Enter'); else if (how === 'space') await page.keyboard.press('Space'); else await add.click();
    const fc = await p; console.log(how, 'chooser after', Date.now() - t0, 'ms multiple', fc.isMultiple());
    await fc.setFiles([]);
  } catch (e) { console.log(how, 'NO chooser', e.message.split('\n')[0]); }
  await page.waitForTimeout(500);
}
await browser.close();
