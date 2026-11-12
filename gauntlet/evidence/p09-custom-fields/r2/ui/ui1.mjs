// Critic p09 r2: add a number field by keyboard on the custom fields screen, in English or Arabic.
import { chromium } from '/home/shan/critic/p09-custom-fields-r2/gauntlet/compare/node_modules/playwright-core/index.mjs';
const B = 'http://localhost:20950', OUT = '/home/shan/evidence-staging/p09-custom-fields/r2/ui/';
const L = process.env.TAG || 'en';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, acceptDownloads: true, locale: L === 'ar' ? 'ar-AE' : 'en-GB' });
const page = await ctx.newPage();
const log = (...a) => console.log(...a);
page.on('pageerror', e => log('PAGEERROR', e.message));
page.on('response', r => { if (r.status() >= 400) log('HTTP', r.status(), r.request().method(), r.url().slice(0, 200)); });
await page.goto(B + '/');
await page.locator('input[name="email"]:focus').waitFor();
await page.keyboard.type(process.env.WHO || 'admin@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.locator('main h1').waitFor();
await page.goto(B + '/customfields/fields');
await page.locator('main h1').waitFor(); await page.waitForTimeout(1500);
log('dir', await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang));
log('h1', await page.locator('main h1').innerText());
await page.keyboard.press('Alt+n'); await page.waitForTimeout(1200);
log('focused after Alt+N', await page.evaluate(() => { const e = document.activeElement; return e?.tagName + ' ' + (e?.getAttribute('aria-label') || e?.name || e?.id || ''); }));
const formText = (await page.locator('main').innerText()).replace(/\n+/g, ' / ');
log('form', formText.slice(0, 1200));
// fill by tabbing through: print the tab order of the form
const order = [];
for (let i = 0; i < 18; i++) {
  order.push(await page.evaluate(() => { const e = document.activeElement; const lab = e?.labels?.[0]?.innerText || e?.getAttribute('aria-label') || e?.innerText?.slice(0, 30) || ''; return `${e?.tagName}:${e?.type || ''}:${lab.replace(/\n/g, ' ')}`; }));
  await page.keyboard.press('Tab');
}
log('tab order', order.join(' | '));
await page.screenshot({ path: OUT + L + '-new-field-form.jpg', type: 'jpeg', quality: 60 });
await browser.close();
