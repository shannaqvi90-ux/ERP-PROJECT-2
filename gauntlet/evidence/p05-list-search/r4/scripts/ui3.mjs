// Critic p05 r4: column chooser, views, grouping, any-match, End on 100k, bulk action, by keyboard where possible.
import { chromium } from '/home/shan/critic/p05-list-search-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
const base = 'http://localhost:20550';
const out = '/home/shan/evidence-staging/p05-list-search/r4/';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await ctx.newPage();
page.on('dialog', d => { console.log('   [browser dialog]', d.type(), d.message()); d.accept(); });
const log = (...a) => console.log(...a);
const shot = async n => page.screenshot({ path: out + n, type: 'jpeg', quality: 70 });
const focus = () => page.evaluate(() => { const e = document.activeElement; return e ? `${e.tagName.toLowerCase()}${e.getAttribute('role') ? '[role=' + e.getAttribute('role') + ']' : ''} "${(e.getAttribute('aria-label') || e.textContent || '').trim().slice(0, 60)}"` : null; });
const status = () => page.locator('.list-count').allInnerTexts().then(a => a.join(' | ')).catch(() => '');
await page.goto(base + '/');
await page.getByLabel('E-mail', { exact: true }).fill('admin@alnoor.example');
await page.getByLabel('Password', { exact: true }).fill('Demo-Pass-2026');
await page.keyboard.press('Enter');
await page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first().click();
await page.waitForTimeout(1500);
// End on 100k rows: virtualised
await page.keyboard.press('ArrowDown');
let t0 = Date.now();
await page.keyboard.press('End');
await page.waitForFunction(() => [...document.querySelectorAll('[role="row"]')].some(r => /Omar|Layla|Fatima|Mariam/.test(r.textContent) === false && r.getAttribute('aria-rowindex') === '100005'), null, { timeout: 15000 }).catch(e => log('   end wait:', e.message.split('\n')[0]));
log('1 End on 100,004 rows took', Date.now() - t0, 'ms; DOM rows', await page.locator('[role="row"]').count(), 'last rowindex', await page.evaluate(() => [...document.querySelectorAll('[role="row"]')].map(r => r.getAttribute('aria-rowindex')).filter(Boolean).slice(-1)[0]));
const lastRow = await page.evaluate(() => [...document.querySelectorAll('[role="row"]')].slice(-1)[0]?.innerText.replace(/\s+/g, ' '));
log('   last row text', lastRow);
await shot('05-end-of-100k-rows.jpg');
await page.keyboard.press('Home');
await page.waitForTimeout(800);
// Columns chooser
await page.getByRole('button', { name: 'Columns' }).click();
await page.waitForTimeout(400);
log('2 columns chooser focus', await focus(), 'text', (await page.locator('[role="dialog"]').last().innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 300));
await shot('06-column-chooser.jpg');
await page.keyboard.press('Escape');
// Group by Language via column menu
await page.getByRole('button', { name: 'Options for the column Language' }).click();
await page.getByRole('menuitem', { name: /Group by/ }).click();
await page.waitForTimeout(1500);
log('3 grouped: url', page.url(), 'rows', (await page.locator('[role="row"]').allInnerTexts()).map(s => s.replace(/\s+/g, ' ')).slice(0, 5));
await shot('07-grouped-by-language.jpg');
// Enter on a group drills in
await page.locator('[role="grid"]').focus();
await page.keyboard.press('ArrowDown');
await page.keyboard.press('Enter');
await page.waitForTimeout(1200);
log('4 drill into group: url', page.url(), 'status', await status());
// Add a second condition on Status and switch to "any"
await page.getByRole('button', { name: 'Options for the column Status' }).click();
await page.getByRole('menuitem', { name: /Filter/ }).click();
await page.waitForTimeout(400);
const ed = page.locator('.list-colpopover, [role="dialog"]').last();
log('5 status editor', (await ed.innerText()).replace(/\s+/g, ' '));
const boxes = ed.locator('input[type="checkbox"]');
const n = await boxes.count();
if (n > 1) await boxes.nth(1).check(); else if (n) await boxes.nth(0).check();
await ed.getByRole('button', { name: 'Apply' }).click();
await page.waitForTimeout(1200);
log('6 two conditions: url', decodeURIComponent(page.url()), 'status', await status());
const matchBtn = page.locator('.list-match');
log('7 match button', await matchBtn.count() ? await matchBtn.innerText() : 'none');
if (await matchBtn.count()) { await matchBtn.click(); await page.waitForTimeout(1200); log('8 after any: url', decodeURIComponent(page.url()), 'status', await status()); }
await shot('08-filter-chips-match-any.jpg');
// Save view as shared + default
await page.getByRole('button', { name: /^View:/ }).click();
await page.waitForTimeout(300);
log('9 view menu', (await page.locator('[role="menu"], [role="dialog"]').last().innerText()).replace(/\s+/g, ' ').slice(0, 300));
const save = page.getByRole('menuitem', { name: /Save/ }).or(page.getByRole('button', { name: /Save as|Save view/ })).first();
if (await save.count()) {
  await save.click(); await page.waitForTimeout(300);
  const dlg = page.locator('[role="dialog"]').last();
  log('10 save dialog', (await dlg.innerText()).replace(/\s+/g, ' ').slice(0, 300));
  await dlg.getByRole('textbox').first().fill('Critic any view');
  const share = dlg.getByRole('checkbox', { name: /Share|everyone/i });
  if (await share.count()) await share.check();
  const def = dlg.getByRole('checkbox', { name: /default|Open/i });
  if (await def.count()) await def.check();
  await dlg.getByRole('button', { name: /^Save$/ }).click();
  await page.waitForTimeout(1200);
  log('11 after save: url', decodeURIComponent(page.url()), 'view button', await page.getByRole('button', { name: /^View:/ }).innerText());
}
// Reload the list fresh: does the default shared view open?
await page.goto(base + '/identity/users');
await page.waitForTimeout(2000);
log('12 fresh open: url', decodeURIComponent(page.url()), 'view', await page.getByRole('button', { name: /^View:/ }).innerText(), 'status', await status());
await browser.close();
