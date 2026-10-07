// Critic p05 r4: Arabic list, End timing, viewer and no-access users.
import { chromium } from '/home/shan/critic/p05-list-search-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
const base = 'http://localhost:20550';
const out = '/home/shan/evidence-staging/p05-list-search/r4/';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const log = (...a) => console.log(...a);
async function session(email) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();
  await page.goto(base + '/');
  await page.getByLabel(/^(E-mail|البريد الإلكتروني)$/).first().fill(email);
  await page.getByLabel(/^(Password|كلمة المرور)$/).first().fill('Demo-Pass-2026');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(2000);
  return page;
}
// Arabic administrator
let page = await session('admin.ar@alnoor.example');
await page.goto(base + '/identity/users');
await page.waitForTimeout(2000);
log('AR dir', await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang));
log('AR headers', await page.locator('[role="columnheader"]').allInnerTexts());
log('AR count', await page.locator('.list-count').allInnerTexts());
await page.screenshot({ path: out + '09-users-list-ar.jpg', type: 'jpeg', quality: 70 });
// Arabic search with a spelling variant, as you type, then Enter
await page.keyboard.type('ماجد انيل بيلاى', { delay: 40 });
await page.waitForTimeout(1500);
log('AR search variant count', await page.locator('.list-count').allInnerTexts(), 'rows', (await page.locator('[role="row"]').allInnerTexts()).slice(1, 3).map(s => s.replace(/\s+/g, ' ')));
await page.screenshot({ path: out + '10-arabic-variant-search.jpg', type: 'jpeg', quality: 70 });
await page.keyboard.press('Enter');
await page.waitForTimeout(1200);
log('AR Enter opens', page.url(), (await page.locator('[role="dialog"], aside, [role="complementary"]').first().innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 200));
await page.keyboard.press('Escape');
// Column menu in Arabic
await page.locator('[role="columnheader"] button[aria-haspopup], [role="columnheader"] button').nth(1).click().catch(() => {});
await page.waitForTimeout(300);
log('AR column menu', (await page.locator('[role="menu"]').allInnerTexts()).map(s => s.replace(/\s+/g, ' ')));
// Latin text anywhere in the Arabic chrome (outside data cells)?
const latin = await page.evaluate(() => {
  const skip = el => el.closest('[role="gridcell"], [role="row"] td, input, .list-chip, [data-user-content]');
  const out = new Set();
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  while (walker.nextNode()) { const n = walker.currentNode; const t = n.textContent.trim(); if (/[A-Za-z]{3,}/.test(t) && !skip(n.parentElement)) out.add(t.slice(0, 60)); }
  return [...out];
});
log('AR latin text in chrome', JSON.stringify(latin));
await page.context().close();
// End timing in English
page = await session('admin@alnoor.example');
await page.goto(base + '/identity/users');
await page.waitForTimeout(2000);
await page.keyboard.press('ArrowDown');
let t0 = Date.now();
await page.keyboard.press('End');
await page.waitForFunction(() => { const rows = [...document.querySelectorAll('[role="row"][aria-rowindex]')]; const last = rows.find(r => r.getAttribute('aria-rowindex') === '100005'); return last && last.innerText.trim().length > 5; }, null, { timeout: 20000 }).catch(e => log('   wait', e.message.split('\n')[0]));
log('End: last row filled after', Date.now() - t0, 'ms', await page.evaluate(() => [...document.querySelectorAll('[role="row"][aria-rowindex="100005"]')].map(r => r.innerText.replace(/\s+/g, ' '))[0]));
await page.screenshot({ path: out + '05-end-of-100k-rows.jpg', type: 'jpeg', quality: 70 });
// PageUp x3 timing
t0 = Date.now();
for (let i = 0; i < 50; i++) await page.keyboard.press('PageUp');
await page.waitForTimeout(100);
await page.waitForFunction(() => ![...document.querySelectorAll('[role="row"][aria-rowindex]')].some(r => /Loading|تحميل/.test(r.innerText)), null, { timeout: 20000 }).catch(e => log('   wait', e.message.split('\n')[0]));
log('50 x PageUp then filled after', Date.now() - t0, 'ms');
await page.context().close();
// Viewer
page = await session('viewer@alnoor.example');
await page.goto(base + '/identity/users');
await page.waitForTimeout(2000);
log('viewer buttons', (await page.getByRole('button').allInnerTexts()).filter(Boolean).slice(0, 20));
await page.locator('[role="grid"]').focus(); await page.keyboard.press('Space');
log('viewer selection bar', (await page.locator('.list-selectionbar').innerText().catch(() => 'none')).replace(/\s+/g, ' '));
await page.getByRole('button', { name: /^View:/ }).click();
await page.getByRole('menuitem', { name: /Save/ }).first().click().catch(() => {});
await page.waitForTimeout(300);
log('viewer save dialog', (await page.locator('[role="dialog"]').last().innerText().catch(() => 'none')).replace(/\s+/g, ' '));
await page.context().close();
// No access
page = await session('noaccess@alnoor.example');
await page.goto(base + '/identity/users');
await page.waitForTimeout(2000);
log('noaccess users page', (await page.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 200));
await browser.close();
