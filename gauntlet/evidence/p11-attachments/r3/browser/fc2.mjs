import { chromium } from '/home/shan/critic/p11-attachments-r3-plant/gauntlet/compare/node_modules/playwright-core/index.mjs';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome', headless: process.argv[3] !== 'headed' });
const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
await page.goto('http://localhost:21150/'); await page.evaluate((l) => { localStorage.clear(); localStorage.setItem('erp.language', l); }, process.argv[2] || 'en'); await page.goto('http://localhost:21150/');
await page.locator('input[name="email"]').waitFor(); await page.keyboard.type('admin@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.waitForTimeout(2500);
await page.goto('http://localhost:21150/contacts/contacts/0188758b-3388-762c-ac40-a20f4083549e'); await page.waitForTimeout(3000);
await page.locator('h2').first().click();
await page.keyboard.press('Alt+KeyA'); await page.waitForTimeout(500);
console.log('focused', await page.evaluate(() => document.activeElement?.outerHTML.slice(0, 80)));
await page.evaluate(() => { window.__keys = []; document.addEventListener('keydown', e => window.__keys.push(['down', e.key, e.defaultPrevented]), true); document.addEventListener('keydown', e => window.__keys.push(['down-bubble', e.key, e.defaultPrevented])); document.addEventListener('click', e => window.__keys.push(['click', e.target.tagName, e.target.textContent?.slice(0,20)]), true); });
const t0 = Date.now();
try { const p = page.waitForEvent('filechooser', { timeout: 6000 }); await page.keyboard.press('Enter'); await p; console.log('Alt+A then Enter: chooser', Date.now() - t0, 'ms'); }
catch (e) { console.log('Alt+A then Enter: NO chooser'); }
console.log(JSON.stringify(await page.evaluate(() => window.__keys)));
await browser.close();
