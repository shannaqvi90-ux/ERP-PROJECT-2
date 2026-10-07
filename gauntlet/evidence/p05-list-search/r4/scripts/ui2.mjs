// Critic p05 r4: the users list driven as a user would, by keyboard, in English.
import { chromium } from '/home/shan/critic/p05-list-search-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
const base = 'http://localhost:20550';
const out = '/home/shan/evidence-staging/p05-list-search/r4/';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
await ctx.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: base });
const page = await ctx.newPage();
const log = (...a) => console.log(...a);
const shot = async n => page.screenshot({ path: out + n, type: 'jpeg', quality: 70 });
const focus = () => page.evaluate(() => { const e = document.activeElement; return e ? `${e.tagName.toLowerCase()}${e.getAttribute('role') ? '[role=' + e.getAttribute('role') + ']' : ''} "${(e.getAttribute('aria-label') || e.textContent || '').trim().slice(0, 60)}"` : null; });
const status = () => page.locator('.list-count, [role="status"]').allInnerTexts().then(a => a.filter(Boolean).join(' | '));
const domRows = () => page.locator('[role="row"]').count();
await page.goto(base + '/');
await page.getByLabel('E-mail', { exact: true }).fill('admin@alnoor.example');
await page.getByLabel('Password', { exact: true }).fill('Demo-Pass-2026');
await page.keyboard.press('Enter');
await page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first().click();
await page.waitForTimeout(1500);
log('1 arrival focus', await focus(), 'status', await status(), 'DOM rows', await domRows());
// Quick search as you type (no Enter)
let t0 = Date.now();
await page.keyboard.type('pillai', { delay: 60 });
await page.waitForFunction(() => /1,651/.test(document.body.innerText), null, { timeout: 10000 }).catch(() => {});
log('2 typed pillai, no Enter: status', await status(), 'after', Date.now() - t0, 'ms; url', page.url());
// Arrow into grid, move, open by Enter
await page.keyboard.press('ArrowDown');
log('3 ArrowDown from search -> focus', await focus());
await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown');
const activeRow = await page.evaluate(() => document.querySelector('[role="row"][aria-selected="true"], [role="row"].active, [role="row"][data-active="true"]')?.textContent?.slice(0, 80));
log('4 active row after 2 downs', activeRow);
await page.keyboard.press('Enter');
await page.waitForTimeout(800);
log('5 Enter -> url', page.url(), 'dialog?', await page.locator('[role="dialog"], [role="complementary"], aside').count());
await shot('02-record-open-from-keyboard.jpg');
await page.keyboard.press('Escape');
await page.waitForTimeout(400);
log('6 Esc -> url', page.url(), 'focus', await focus());
// Selection: Space, Shift+Down, Ctrl+A twice
await page.keyboard.press('Space');
await page.keyboard.press('Shift+ArrowDown'); await page.keyboard.press('Shift+ArrowDown');
log('7 selection bar', await page.locator('.list-selectionbar').innerText().catch(() => 'none'));
await page.keyboard.press('Control+a');
log('8 Ctrl+A', await page.locator('.list-selectionbar').innerText().catch(() => 'none'));
await page.keyboard.press('Control+a');
log('9 Ctrl+A again', await page.locator('.list-selectionbar').innerText().catch(() => 'none'));
await shot('03-select-all-matching-bulk.jpg');
const bulkButtons = await page.locator('.list-selectionbar button').allInnerTexts();
log('10 bulk buttons', bulkButtons);
await page.keyboard.press('Escape');
log('11 Esc clears selection', await page.locator('.list-selectionbar').count());
// Header sort by keyboard: Tab from search to header controls
await page.keyboard.press('/');
await page.keyboard.press('Control+a'); await page.keyboard.press('Backspace');
await page.waitForTimeout(800);
const tabs = [];
for (let i = 0; i < 14; i++) { await page.keyboard.press('Tab'); tabs.push(await focus()); }
log('12 Tab order from search', JSON.stringify(tabs));
// Find the sort button for "E-mail" by keyboard: shift-tab back until reached
const sortBtn = page.getByRole('button', { name: /^E-mail/ }).first();
await sortBtn.focus();
log('13 focused', await focus());
const urlBefore = page.url();
await page.keyboard.press('Enter');
await page.waitForTimeout(1200);
log('14 Enter on E-mail header -> url', page.url(), 'changed', page.url() !== urlBefore, 'first row', (await page.locator('[role="row"]').nth(1).innerText()).replace(/\s+/g, ' ').slice(0, 80));
await page.keyboard.press('Space');
await page.waitForTimeout(1200);
log('15 Space on E-mail header -> url', page.url(), 'first row', (await page.locator('[role="row"]').nth(1).innerText()).replace(/\s+/g, ' ').slice(0, 80));
// Column menu by keyboard
const menuBtn = page.getByRole('button', { name: /Options for the column Language|Language.*options|column menu.*Language/i }).first();
const menuCount = await menuBtn.count();
log('16 column menu button for Language exists', menuCount, menuCount ? await menuBtn.getAttribute('aria-label') : '');
if (menuCount) {
  await menuBtn.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(400);
  log('17 menu open focus', await focus(), 'items', await page.locator('[role="menu"] [role="menuitem"], [role="menu"] button').allInnerTexts());
  // pick "Filter" via arrows
  const items = await page.locator('[role="menu"] [role="menuitem"], [role="menu"] button').allInnerTexts();
  const fi = items.findIndex(x => /filter/i.test(x));
  for (let i = 0; i < fi; i++) await page.keyboard.press('ArrowDown');
  log('18 focus before Enter', await focus());
  await page.keyboard.press('Enter'); await page.waitForTimeout(500);
  log('19 filter editor focus', await focus());
  await shot('04-header-filter-editor.jpg');
  const ed = await page.evaluate(() => document.activeElement?.closest('[role="dialog"], .list-colpopover, form, [role="menu"]')?.innerText?.slice(0, 300));
  log('20 editor text', JSON.stringify(ed));
}
await browser.close();
