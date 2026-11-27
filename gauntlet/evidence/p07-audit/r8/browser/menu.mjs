// r8 critic: what a column's options menu offers on the audit log, and the date filter by keyboard. node menu.mjs <en|ar> <column> <menu item regex> <typed...>
import { chromium } from '/home/shan/critic/p07-audit-r8/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20750', OUT = '/home/shan/evidence-staging/p07-audit/r8/browser';
const [lang, col, item, ...typed] = process.argv.slice(2);
const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
const errors = []; page.on('response', r => { if (r.status() >= 400) errors.push(`${r.status()} ${decodeURIComponent(r.url()).slice(0, 200)}`); });
const apis = []; page.on('requestfinished', r => { const u = decodeURIComponent(r.url()); if (u.includes('/api/audit/log?')) apis.push(`${Math.round(r.timing().responseEnd)}ms ${u.replace(BASE, '').slice(0, 220)}`); });
await page.goto(BASE + '/');
await page.locator('input[name="email"]').fill(lang === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example'); await page.locator('input[name="password"]').fill('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.waitForSelector('nav'); await page.waitForTimeout(800);
await page.keyboard.press('g'); await page.keyboard.press('l');
await page.waitForSelector('[role="grid"] [role="row"] >> nth=3', { timeout: 30000 });
console.log('header buttons', JSON.stringify(await page.locator('[role="columnheader"] button').evaluateAll(bs => bs.map(b => b.getAttribute('aria-label') || b.innerText))));
const btn = page.locator(`[role="columnheader"] button[aria-label]`).filter({ has: page.locator('xpath=self::*') }).and(page.locator(`[aria-label$=" ${col}"]`)).first();
await btn.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(400);
const items = await page.locator('[role=menu] [role=menuitem]').allInnerTexts(); console.log('menu', JSON.stringify(items));
const idx = items.findIndex(t => new RegExp(item).test(t));
for (let i = 0; i < idx; i++) await page.keyboard.press('ArrowDown');
await page.keyboard.press('Enter'); await page.waitForTimeout(500);
console.log('dialog', (await page.locator('[role="dialog"]').innerText().catch(() => '(none)')).replace(/\s+/g, ' ').slice(0, 300));
console.log('focus', await page.evaluate(() => document.activeElement?.outerHTML.slice(0, 200)));
for (const t of typed) { if (t.startsWith('key:')) await page.keyboard.press(t.slice(4)); else await page.keyboard.type(t); await page.waitForTimeout(150); }
console.log('focus after typing', await page.evaluate(() => document.activeElement?.outerHTML.slice(0, 200)), 'values', JSON.stringify(await page.locator('[role="dialog"] input').evaluateAll(is => is.map(i => i.value))));
await page.waitForTimeout(1500);
console.log('count/chips', (await page.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 400));
await page.screenshot({ path: `${OUT}/${lang}-menu-${Date.now()}.jpg`, type: 'jpeg', quality: 55 });
console.log(apis.slice(-3).join('\n')); console.log('errors', JSON.stringify(errors));
await browser.close();
