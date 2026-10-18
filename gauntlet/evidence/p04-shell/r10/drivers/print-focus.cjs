// Critic p04 r10: keyboard user on the Arabic users list tabs to a column header, then prints (Chromium's print to PDF).
const { chromium } = require('/home/shan/critic/p04-shell-r10-plant/gauntlet/compare/node_modules/playwright-core');
const BASE = 'http://localhost:20450', OUT = '/home/shan/evidence-staging/p04-shell/r10';
const exe = require('os').homedir() + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
(async () => {
  const browser = await chromium.launch({ executablePath: exe });
  const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US', timezoneId: 'Asia/Dubai' })).newPage();
  await page.goto(BASE + '/'); await page.waitForSelector('input[name=email]');
  await page.keyboard.type('admin.ar@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.waitForTimeout(2500);
  await page.keyboard.press('g'); await page.waitForTimeout(150); await page.keyboard.press('u'); await page.waitForTimeout(2000);
  for (let i = 0; i < 6; i++) await page.keyboard.press('Tab');
  const focused = await page.evaluate(() => document.activeElement.textContent.trim());
  await page.emulateMedia({ media: 'print' });
  const outline = await page.evaluate(() => { const cs = getComputedStyle(document.activeElement); return `${cs.outlineStyle} ${cs.outlineWidth} ${cs.outlineColor}`; });
  await page.pdf({ path: `${OUT}/print-users-ar-after-keyboard.pdf`, format: 'A4', landscape: true, printBackground: true });
  console.log(JSON.stringify({ focused, outlineInPrintMedia: outline }));
  await browser.close();
})();
