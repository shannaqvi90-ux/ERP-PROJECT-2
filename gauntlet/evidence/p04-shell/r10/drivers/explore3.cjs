// Critic p04 r10, third pass: does the users list show the signed-in user's own language right after Alt+L?
// Does printing after keyboard use print a focus ring? Language toggle by mouse; sign-out focus.
const { chromium } = require('/home/shan/critic/p04-shell-r10-plant/gauntlet/compare/node_modules/playwright-core');
const fs = require('fs');
const BASE = 'http://localhost:20450', OUT = '/home/shan/evidence-staging/p04-shell/r10';
const exe = require('os').homedir() + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
const res = {};
(async () => {
  const browser = await chromium.launch({ executablePath: exe });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US', timezoneId: 'Asia/Dubai' });
  const page = await ctx.newPage();
  await page.goto(BASE + '/'); await page.waitForSelector('input[name=email]');
  await page.keyboard.type('admin@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.waitForTimeout(2500);
  await page.keyboard.press('g'); await page.waitForTimeout(150); await page.keyboard.press('u'); await page.waitForTimeout(2000);
  const ownRow = () => page.evaluate(() => { const r = [...document.querySelectorAll('main tbody tr')].find(tr => tr.textContent.includes('admin@alnoor.example')); return r ? [...r.querySelectorAll('td')].map(td => td.textContent.trim()).join(' | ') : null; });
  res.ownRowBefore = await ownRow();
  await page.keyboard.press('Alt+l'); await page.waitForTimeout(2000);
  res.ownRowAfterSwitch = await ownRow();
  await page.waitForTimeout(5000);
  res.ownRowAfter7s = await ownRow();
  // focus a header then print
  for (let i = 0; i < 6; i++) await page.keyboard.press('Tab');
  res.focused = await page.evaluate(() => document.activeElement.textContent.trim().slice(0, 40));
  await page.emulateMedia({ media: 'print' });
  res.printOutline = await page.evaluate(() => { const cs = getComputedStyle(document.activeElement); return cs.outlineStyle + ' ' + cs.outlineWidth + ' ' + cs.outlineColor; });
  await page.emulateMedia({ media: 'screen' });
  await page.keyboard.press('Alt+l'); await page.waitForTimeout(800);
  await browser.close();
  fs.writeFileSync(`${OUT}/explore3.json`, JSON.stringify(res, null, 2)); console.log(JSON.stringify(res, null, 2));
})().catch((e) => { console.error(e); process.exit(1); });
