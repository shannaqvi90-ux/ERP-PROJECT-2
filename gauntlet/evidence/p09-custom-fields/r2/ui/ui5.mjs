// Contacts list: add the custom column, filter by it from its header menu, sort, print/export CSV, XLSX and PDF.
import { open, log, shot, OUT } from './common.mjs';
const L = process.env.TAG || 'en';
const T = L === 'ar' ? { cols: 'الأعمدة', field: 'التغطية التأمينية', done: 'تم', filter: /تصفية/, print: /طباعة/ } : { cols: 'Columns', field: 'Insurance cover', done: 'Done', filter: /Filter/, print: /Print or export/ };
const { browser, page } = await open(L);
page.on('response', r => { if (r.url().includes('/api/reports')) log('REPORT', r.status(), decodeURIComponent(r.url()).slice(0, 300)); });
await page.goto('http://localhost:20950/contacts/contacts');
await page.locator('table[role=grid] tbody tr').first().waitFor();
log('dir', await page.evaluate(() => document.documentElement.dir));
await page.locator('main').getByRole('button', { name: T.cols, exact: true }).click();
await page.waitForTimeout(500);
await page.getByRole('checkbox', { name: T.field }).check();
await page.getByRole('button', { name: T.done, exact: true }).click().catch(() => page.keyboard.press('Escape'));
await page.waitForTimeout(1000);
const hdr = page.locator('table[role=grid] thead th', { hasText: T.field });
log('header present', await hdr.count());
await hdr.locator('button').last().click(); await page.waitForTimeout(500);
log('menu', (await page.locator('[role=menu]').last().innerText().catch(() => '-')).replace(/\n/g, ' / '));
await page.getByRole('menuitem', { name: T.filter }).first().click(); await page.waitForTimeout(600);
const dlg = page.locator('[role=dialog]').last();
log('filter dialog', (await dlg.innerText().catch(() => '-')).replace(/\n/g, ' / ').slice(0, 400));
await shot(page, L + '-number-filter-dialog');
// operator + value: try to pick ">=" and type 5000
const sel = dlg.locator('select').first(); if (await sel.count()) { log('ops', JSON.stringify(await sel.evaluate(e => [...e.options].map(o => o.value)))); await sel.selectOption({ index: Math.min(3, (await sel.evaluate(e => e.options.length)) - 1) }); }
const inp = dlg.locator('input:not([type=checkbox])').first(); await inp.fill('5000');
await page.keyboard.press('Enter'); await page.waitForTimeout(2000);
log('url', decodeURIComponent(page.url()));
log('count text', await page.locator('main').getByText(/\d+ (contacts|contact)|جهة|جهات/).first().innerText().catch(() => '-'));
const cells = await page.locator('table[role=grid] tbody tr').evaluateAll(rs => rs.slice(0, 5).map(r => r.innerText.replace(/\s+/g, ' ').slice(0, 160)));
log('rows', JSON.stringify(cells));
await shot(page, L + '-filtered-by-custom-number');
for (const fmt of ['CSV', 'Excel', 'PDF'].filter(f => process.env.FMTS ? process.env.FMTS.includes(f) : true)) {
  await page.locator('main').getByRole('button', { name: T.print }).first().click(); await page.waitForTimeout(800);
  if (fmt === 'CSV') { log('print menu', (await page.locator('[role=menu],[role=dialog]').last().innerText().catch(() => '-')).replace(/\n/g, ' / ').slice(0, 500)); await shot(page, L + '-print-menu'); }
  const item = page.locator('[role=menu] [role=menuitem], [role=dialog] button').filter({ hasText: new RegExp(fmt === 'Excel' ? 'Excel|XLSX|إكسل' : fmt) }).first();
  const dl = page.waitForEvent('download', { timeout: 60000 }).catch(() => null);
  await item.click().catch(e => log('no item', fmt, e.message.slice(0, 120)));
  const d = await dl;
  if (d) { const f = OUT + L + '-export.' + (fmt === 'Excel' ? 'xlsx' : fmt.toLowerCase()); await d.saveAs(f); log('download', fmt, await d.suggestedFilename(), await d.failure()); }
  else { log('no download', fmt); await shot(page, L + '-no-download-' + fmt); await page.keyboard.press('Escape'); }
  await page.waitForTimeout(800);
}
await browser.close();
