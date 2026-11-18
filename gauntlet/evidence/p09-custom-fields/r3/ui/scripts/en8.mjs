import { open, shot, BASE, OUT } from './lib.mjs';
const lang = process.argv[2] || 'en';
const ar = lang === 'ar';
const { browser, page, errors } = await open(ar ? 'admin.ar@alnoor.example' : 'admin@alnoor.example');
const log = (...a) => console.log(...a);
const L = ar ? { cols: 'الأعمدة', done: 'تم', opts: 'خيارات العمود ', filter: 'تصفية…', print: 'طباعة أو تصدير', show: 'إظهار ' } : { cols: 'Columns', done: 'Done', opts: 'Options for the column ', filter: 'Filter…', print: 'Print or export', show: 'Show ' };
const C = ar ? { ins: 'التغطية التأمينية 31', exp: 'انتهاء الرخصة 31', inc: 'شروط التسليم 14727', par: 'الحساب الرئيسي 31' } : { ins: 'Insurance cover 31', exp: 'Licence expiry 31', inc: 'Incoterm 14727', par: 'Parent account 31' };
const reqs = []; page.on('request', r => { if (r.url().includes('/api/reports/')) reqs.push(decodeURIComponent(r.url().replace(/^.*\/api/, '/api'))); });
await page.keyboard.press('g'); await page.keyboard.press('o'); await page.waitForTimeout(2500);
await page.getByRole('button', { name: L.cols }).focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(800);
const labels = await page.locator('[role=dialog] input[type=checkbox]').evaluateAll(xs => xs.map(x => x.getAttribute('aria-label')));
log('chooser labels sample', labels.slice(-6).join(' | '));
for (const c of [C.ins, C.exp, C.inc, C.par]) { const l = labels.find(x => x.endsWith(c)); await page.getByLabel(l, { exact: true }).focus(); await page.keyboard.press('Space'); }
await page.getByRole('button', { name: L.done, exact: true }).focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
const optLabels = await page.locator('[role=columnheader] button[aria-label]').evaluateAll(xs => xs.map(x => x.getAttribute('aria-label')));
const opt = c => optLabels.find(x => x.endsWith(c));
await page.getByRole('button', { name: opt(C.ins), exact: true }).focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(500);
const items = await page.locator('[role=menuitem]').allInnerTexts(); log('menu items', items.join(' | '));
await page.locator('[role=menuitem]').nth(2).focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(800);
await page.keyboard.type(ar ? '٢٠٠٠٠٠' : '200000'); await page.keyboard.press('Enter'); await page.waitForTimeout(2500);
log('filtered:', (await page.locator('main').innerText()).slice(0, 400).replace(/\n/g, ' | '));
await shot(page, `${lang}-filtered-custom-number`);
await page.getByRole('button', { name: L.print }).focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(1000);
await page.waitForTimeout(500);
const fmts = await page.locator('[role=menuitem]').allInnerTexts();
await page.keyboard.press('Escape');
for (let k = 0; k < fmts.length; k++) {
  { await page.getByRole('button', { name: L.print }).focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(600); }
  const dl = page.waitForEvent('download', { timeout: 120000 }).catch(e => null);
  await page.locator('[role=menuitem]').nth(k).focus(); await page.keyboard.press('Enter');
  const d = await dl; if (!d) { log('NO DOWNLOAD', fmts[k]); continue; }
  const f = `${OUT}/${lang}-export-${k}-${d.suggestedFilename()}`; await d.saveAs(f); log('downloaded', fmts[k], f);
  await page.waitForTimeout(800);
}
log(reqs.join('\n'));
log(errors);
await browser.close();
