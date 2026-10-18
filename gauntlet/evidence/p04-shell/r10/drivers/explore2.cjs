// Critic p04 r10, second pass: Arabic help sheet, Arabic-Indic digits, palette keyboard flow in Arabic, record open by keyboard.
const { chromium } = require('/home/shan/critic/p04-shell-r10-plant/gauntlet/compare/node_modules/playwright-core');
const fs = require('fs');
const BASE = 'http://localhost:20450', OUT = '/home/shan/evidence-staging/p04-shell/r10';
const exe = require('os').homedir() + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
const res = {};
(async () => {
  const browser = await chromium.launch({ executablePath: exe });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US', timezoneId: 'Asia/Dubai' });
  const page = await ctx.newPage(); const errors = [];
  page.on('pageerror', (e) => errors.push(String(e))); page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
  await page.goto(BASE + '/'); await page.waitForSelector('input[name=email]');
  await page.keyboard.type('admin.ar@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.waitForTimeout(2500);
  res.home = await page.evaluate(() => ({ dir: document.documentElement.dir, lang: document.documentElement.lang, h1: document.querySelector('h1')?.textContent }));
  await page.keyboard.press('Control+/'); await page.waitForTimeout(600);
  res.helpAr = await page.evaluate(() => document.querySelector('[role=dialog]')?.innerText?.slice(0, 1500) || null);
  await page.screenshot({ path: `${OUT}/06-shortcuts-ar.jpg`, type: 'jpeg', quality: 60 });
  await page.keyboard.press('Escape'); await page.waitForTimeout(300);
  res.focusAfterHelp = await page.evaluate(() => document.activeElement?.tagName + ' ' + (document.activeElement?.getAttribute('aria-label') || ''));
  // Arabic-Indic digits via the palette
  await page.keyboard.press('Control+k'); await page.waitForTimeout(300); await page.keyboard.type('أرقام'); await page.waitForTimeout(600);
  res.digitsOptions = await page.evaluate(() => [...document.querySelectorAll('[role=option]')].map(o => o.textContent.trim()));
  await page.keyboard.press('Enter'); await page.waitForTimeout(800);
  // go to users with G U then open the third row by keyboard
  await page.keyboard.press('g'); await page.waitForTimeout(150); await page.keyboard.press('u'); await page.waitForTimeout(2000);
  res.usersCount = await page.evaluate(() => document.querySelector('main')?.innerText.match(/[٠-٩,٬]+ مستخدم\S*/)?.[0]);
  res.usersDates = await page.evaluate(() => [...document.querySelectorAll('main td')].map(t => t.textContent).filter(s => /[٠-٩]/.test(s)).slice(0, 3));
  await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
  res.recordOpen = await page.evaluate(() => ({ url: location.pathname, panel: document.querySelector('[role=dialog], aside, .record-panel')?.innerText?.slice(0, 300) }));
  await page.screenshot({ path: `${OUT}/11-user-record-ar-digits.jpg`, type: 'jpeg', quality: 60 });
  await page.keyboard.press('Escape'); await page.waitForTimeout(500);
  // Breadcrumbs direction and mirrored icons
  res.breadcrumb = await page.evaluate(() => { const b = document.querySelector('nav[aria-label] ol, .breadcrumbs'); return b ? { text: b.innerText, dir: getComputedStyle(b).direction } : null; });
  // turn digits back to Latin
  await page.keyboard.press('Control+k'); await page.waitForTimeout(300); await page.keyboard.type('أرقام'); await page.waitForTimeout(600); await page.keyboard.press('Enter'); await page.waitForTimeout(500);
  res.errors = errors;
  await browser.close();
  fs.writeFileSync(`${OUT}/explore2.json`, JSON.stringify(res, null, 2)); console.log(JSON.stringify(res, null, 2));
})().catch((e) => { console.error(e); process.exit(1); });
