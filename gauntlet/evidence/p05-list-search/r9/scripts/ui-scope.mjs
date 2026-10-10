// Scope walk of the list framework in a real browser: node ui-scope.mjs <baseUrl> <outDir>
import { open, shot } from './pw.mjs';
const base = process.argv[2], out = process.argv[3];
const log = (...a) => console.log(...a);
const active = page => page.evaluate(() => { const e = document.activeElement; return `${e?.tagName}${e?.getAttribute('role') ? '[' + e.getAttribute('role') + ']' : ''} ${(e?.getAttribute('aria-label') || e?.textContent || '').slice(0, 60).replace(/\s+/g, ' ')}`; });
const count = async page => (await page.locator('body').innerText()).match(/[\d,٠-٩٬]+ (users?|مستخدم\S*)/i)?.[0];
const usersLink = page => page.getByRole('navigation').getByRole('link', { name: /^(Users|المستخدمون)$/ }).first();
const step = async (name, fn) => { try { await fn(); } catch (e) { log(`!! ${name}: ${e.message.split('\n')[0].slice(0, 200)}`); } };

if (!process.env.SKIP_ADMIN && !process.env.ONLY_AR) {
  const { browser, page } = await open(base, 'admin@alnoor.example', { width: 1600, height: 900 });
  let t0 = Date.now();
  await usersLink(page).click();
  await page.getByRole('row').nth(5).waitFor();
  log('EN users list rendered in', Date.now() - t0, 'ms; count:', await count(page), '; focus:', await active(page), '; DOM rows:', await page.getByRole('row').count());
  await shot(page, out + '/01-users-list-100k-en.jpg');
  await step('search as you type', async () => {
    t0 = Date.now();
    await page.keyboard.type('map');
    await page.getByRole('row').filter({ hasText: 'Majid Anil Pillai' }).first().waitFor({ timeout: 10000 });
    const rows = await page.getByRole('row').allInnerTexts();
    log('typed initials "map": count', await count(page), 'target row index', rows.findIndex(r => r.includes('Majid Anil Pillai')), 'after', Date.now() - t0, 'ms');
    await shot(page, out + '/02-search-initials.jpg');
    await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
    log('Enter opens best match, panel shows login:', (await page.locator('body').innerText()).includes('majid.pillai.068311'));
    await shot(page, out + '/03-enter-opens-best-match.jpg');
    await page.keyboard.press('Escape'); await page.waitForTimeout(400);
    log('after Esc focus:', await active(page));
  });
  await step('grid keys', async () => {
    const sb = page.getByRole('searchbox').or(page.getByLabel(/search/i)).first();
    await sb.fill(''); await page.waitForTimeout(1000); await sb.focus();
    await page.keyboard.press('ArrowDown'); await page.waitForTimeout(200); log('ArrowDown from search ->', await active(page));
    await page.keyboard.press('End'); await page.waitForTimeout(1500); log('End ->', await active(page));
    await page.keyboard.press('Home'); await page.waitForTimeout(800); log('Home ->', await active(page));
    await page.keyboard.press('Space'); await page.keyboard.press('Shift+ArrowDown'); await page.waitForTimeout(300);
    log('selection:', (await page.locator('body').innerText()).match(/\d+ selected/i)?.[0]);
    await page.keyboard.press('Escape');
  });
  await step('header filter', async () => {
    await page.getByRole('columnheader', { name: /^Language/ }).locator('button').last().click(); await page.waitForTimeout(400);
    log('column menu:', (await page.getByRole('menuitem').allInnerTexts()).join(' | '));
    await page.getByRole('menuitem', { name: /Filter/ }).click(); await page.waitForTimeout(500);
    const dlg = page.getByRole('dialog').last();
    log('filter editor:', (await dlg.innerText()).replace(/\s+/g, ' ').slice(0, 200));
    await shot(page, out + '/04-header-filter-editor.jpg');
    const sel = dlg.locator('select'); const n = await sel.count();
    for (let i = 0; i < n; i++) { const opts = await sel.nth(i).locator('option').allInnerTexts(); if (opts.some(o => /Arabic/.test(o))) await sel.nth(i).selectOption({ label: 'Arabic' }); }
    const cb = dlg.getByRole('checkbox', { name: /Arabic/ }); if (await cb.count()) await cb.first().check();
    await dlg.getByRole('button', { name: 'Apply' }).click(); await page.waitForTimeout(1500);
    log('after filter Language is Arabic: count', await count(page), 'url', page.url());
  });
  await step('group by', async () => {
    await page.getByRole('columnheader', { name: /^Status/ }).locator('button').last().click(); await page.waitForTimeout(400);
    await page.getByRole('menuitem', { name: /Group by/ }).click(); await page.waitForTimeout(1500);
    log('grouped rows:', (await page.getByRole('row').allInnerTexts()).slice(0, 5).map(s => s.replace(/\s+/g, ' ')).join(' || '));
    await shot(page, out + '/05-filter-and-group.jpg');
  });
  await step('save shared default view', async () => {
    await page.getByRole('button', { name: /^View:/ }).click(); await page.waitForTimeout(400);
    await page.getByRole('menuitem', { name: /Save as a new view/ }).click(); await page.waitForTimeout(400);
    const dlg = page.getByRole('dialog').last();
    await dlg.getByLabel('Name').fill('Arabic speakers by status');
    await dlg.getByRole('checkbox', { name: /Share/ }).check();
    await dlg.getByRole('checkbox', { name: /Open this view/ }).check();
    await shot(page, out + '/06-save-shared-view.jpg');
    await dlg.getByRole('button', { name: 'Save' }).click(); await page.waitForTimeout(1200);
    log('after save:', (await page.getByRole('button', { name: /^View:/ }).innerText()));
  });
  await browser.close();
}
if (!process.env.ONLY_AR) {
  const { browser, page } = await open(base, 'viewer@alnoor.example', { width: 1600, height: 900 });
  await usersLink(page).click(); await page.getByRole('row').nth(1).waitFor(); await page.waitForTimeout(1500);
  await shot(page, out + '/09-viewer-opens-shared-default.jpg');
  log('viewer opens Users: view', await page.getByRole('button', { name: /^View:/ }).innerText().catch(() => '?'), 'count', await count(page));
  await step('viewer views menu', async () => {
    await page.getByRole('button', { name: /^View:/ }).click(); await page.waitForTimeout(400);
    log('viewer views menu:', (await page.getByRole('menuitem').allInnerTexts()).join(' | '));
    await page.getByRole('menuitem', { name: /Save as a new view/ }).click(); await page.waitForTimeout(400);
    log('viewer save dialog has share box:', await page.getByRole('dialog').last().getByRole('checkbox', { name: /Share/ }).count());
    await page.keyboard.press('Escape');
  });
  await browser.close();
}
{
  const { browser, page } = await open(base, 'admin.ar@alnoor.example', { width: 1600, height: 900, locale: 'ar-AE' });
  await usersLink(page).click(); await page.getByRole('row').nth(5).waitFor(); await page.waitForTimeout(1200);
  log('AR dir', await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang), 'count', await count(page));
  await shot(page, out + '/07-users-list-ar.jpg');
  await step('arabic search', async () => {
    await page.keyboard.type('ماجد انيل بيلاى'); await page.waitForTimeout(2000);
    const rows = await page.getByRole('row').allInnerTexts();
    log('AR variant search: count', await count(page), 'first row:', rows[1]?.replace(/\s+/g, ' ').slice(0, 100));
    await shot(page, out + '/08-arabic-variant-search.jpg');
  });
  await browser.close();
}
