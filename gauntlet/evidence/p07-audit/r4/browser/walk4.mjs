import { chromium } from '/home/shan/critic/p07-audit-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20750', OUT = '/home/shan/evidence-staging/p07-audit/r4/browser';
const lang = process.argv[2] || 'en';
const email = lang === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example';
const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
const errors = []; page.on('response', async r => { if (r.status() >= 400) errors.push(`${r.status()} ${r.url().slice(0, 200)}`); }); page.on('pageerror', e => errors.push('pageerror ' + e.message));
const apis = []; page.on('requestfinished', r => { if (r.url().includes('/api/audit/log') || r.url().includes('/api/reports')) apis.push(`${Math.round(r.timing().responseEnd)}ms ${decodeURIComponent(r.url().replace(BASE, '')).slice(0, 200)}`); });
await page.goto(BASE + '/');
await page.locator('input[name="email"]').fill(email);
await page.locator('input[name="password"]').fill('Demo-Pass-2026');
await page.keyboard.press('Enter');
await page.waitForSelector('nav'); await page.waitForTimeout(800);
await page.keyboard.press('g'); await page.keyboard.press('l');
await page.waitForSelector('[role="grid"] [role="row"] >> nth=3', { timeout: 30000 });
const dump = async tag => { const d = await page.evaluate(() => { const dlg = document.querySelector('[role="dialog"], [role="menu"], .popover, [role="listbox"]'); return { dlg: dlg ? dlg.innerText.slice(0, 600) : null, focus: document.activeElement?.outerHTML.slice(0, 200) }; }); console.log(tag, JSON.stringify(d)); };
// Keyboard into the "Changed by" filter: Tab from the search box until the header's filter button.
for (let i = 0; i < 12; i++) { await page.keyboard.press('Tab'); const f = await page.evaluate(() => (document.activeElement?.getAttribute('aria-label') || document.activeElement?.innerText || '').slice(0, 60)); console.log('tab', i, f); if (/Changed by|filter/i.test(f) && /Changed/i.test(f)) break; }
await page.keyboard.press('Enter'); await page.waitForTimeout(700);
await dump('after enter on header');
await page.keyboard.press('Enter'); await page.waitForTimeout(700);
await dump('filter dialog');
const html = await page.evaluate(() => (document.querySelector('[role="dialog"]')||document.body).outerHTML.slice(0, 2500));
console.log(html);
await page.screenshot({ path: `${OUT}/${lang}-05-filter-open.jpg`, type: 'jpeg', quality: 70 });
console.log(apis.join('\n')); console.log('errors', JSON.stringify(errors));
await browser.close();
