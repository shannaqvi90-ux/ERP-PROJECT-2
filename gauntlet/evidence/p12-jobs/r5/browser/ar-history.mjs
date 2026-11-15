import { chromium } from '/home/shan/critic/p12-jobs-r5/gauntlet/compare/node_modules/playwright-core/index.mjs';
const base = 'http://localhost:21250';
const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'ar-AE' });
const page = await ctx.newPage();
await page.goto(base + '/'); await page.locator('input[name="email"]:focus').waitFor();
await page.keyboard.type('admin.ar@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.locator('main h1').waitFor();
const id = await page.evaluate(async () => (await (await fetch('/api/jobs/jobs?take=1&filter=' + encodeURIComponent("jobStatus eq 'cancelled' and startedBy eq 'user'"), { headers: { 'X-Erp-Request': '1' } })).json()).items?.[0]?.id);
await page.goto(base + '/jobs/jobs/' + id);
await page.locator('.list-record .jb-status').first().waitFor(); await page.waitForTimeout(1500);
const hdr = await page.locator('.list-record').first().textContent();
console.log('panel head:', hdr.slice(0, 260));
// open the history (record history) to see field changes in Arabic
const hist = page.locator('.list-record button', { hasText: 'السجل' }).first();
if (await hist.count()) { await hist.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(1500); }
const body = await page.locator('body').textContent();
const m = body.match(/(من [a-z_]+ إلى [a-z_]+)/g);
console.log('raw codes in Arabic history:', m ? [...new Set(m)].join(' ; ') : 'none');
await page.evaluate(() => { const el = [...document.querySelectorAll('.list-record *')].find(e => e.children.length === 0 && /من running إلى cancelled/.test(e.textContent)); const all=[...document.querySelectorAll('.list-record *')].filter(e => /running/.test(e.textContent) && e.children.length===0); (all[all.length-1]??el)?.scrollIntoView({block:'center'}); });
await page.waitForTimeout(500);
await page.screenshot({ path: '../08-job-history-ar-raw-status.jpg', type: 'jpeg', quality: 65 });
await browser.close();
