import { open, shot, BASE, OUT } from './lib.mjs';
const { browser, page, errors } = await open('admin.ar@alnoor.example');
const log = (...a) => console.log(...a);
const reqs = []; page.on('request', r => { if (r.url().includes('/api/contacts/contacts?')) reqs.push(decodeURIComponent(r.url().replace(/^.*\/api/, '/api'))); });
await page.keyboard.press('g'); await page.keyboard.press('o'); await page.waitForTimeout(2500);
await page.getByRole('button', { name: 'الأعمدة' }).focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(800);
for (const c of ['إظهار التغطية التأمينية 31', 'إظهار حد الائتمان']) { const l = page.getByLabel(c, { exact: true }); if (await l.count()) { await l.focus(); await page.keyboard.press('Space'); } else log('no', c); }
await page.getByRole('button', { name: 'تم', exact: true }).focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
const optLabels = await page.locator('[role=columnheader] button[aria-label]').evaluateAll(xs => xs.map(x => x.getAttribute('aria-label')));
log(optLabels.join(' | '));
async function tryFilter(col, digits, tag) {
  const o = optLabels.find(x => x.endsWith(col)); if (!o) { log('no column', col); return; }
  await page.getByRole('button', { name: o, exact: true }).focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(500);
  await page.locator('[role=menuitem]').nth(2).focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(800);
  log('input', await page.evaluate(() => document.activeElement?.outerHTML.slice(0, 160)));
  await page.keyboard.type(digits); await page.waitForTimeout(300);
  log('typed value now:', JSON.stringify(await page.evaluate(() => document.activeElement?.value)));
  await shot(page, `x-ar-filter-${tag}`);
  await page.keyboard.press('Enter'); await page.waitForTimeout(2500);
  log(tag, '=>', (await page.locator('main').innerText()).slice(0, 220).replace(/\n/g, ' | '));
  log('last req', reqs.at(-1));
  await page.keyboard.press('Escape');
  const clear = page.getByRole('button', { name: /مسح كل عوامل التصفية/ }); if (await clear.count()) { await clear.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(1500); }
}
await tryFilter('التغطية التأمينية 31', '٢٠٠٠٠٠', 'custom-arabic-digits');
await tryFilter('التغطية التأمينية 31', '200000', 'custom-latin-digits');
await tryFilter('حد الائتمان', '٢٠٠٠٠٠', 'builtin-arabic-digits');
log(errors);
await browser.close();
