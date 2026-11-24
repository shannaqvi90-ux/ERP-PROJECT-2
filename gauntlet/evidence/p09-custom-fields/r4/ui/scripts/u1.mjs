// Create a single select (with default), a date (with default) and a required number (with default) by keyboard.
import { open, shot, text, focused } from './pw.mjs';
const email = process.argv[2] || 'admin@alnoor.example', tag = process.argv[3] || 'en', suffix = process.argv[4] || 'R4';
const ar = tag === 'ar';
const { browser, page, errors } = await open(email);
const go = async () => { await page.keyboard.press('Control+k'); await page.keyboard.type(ar ? 'الحقول المخصصة' : 'Custom fields'); await page.waitForTimeout(1000); await page.keyboard.press('Enter'); await page.waitForTimeout(2000); };
const msgs = () => page.$$eval('[role=alert], [role=status], .field-error, [aria-live]', els => els.map(e => e.textContent.trim()).filter(Boolean).slice(0, 6));
const k = async (key, n = 1) => { for (let i = 0; i < n; i++) await page.keyboard.press(key); };
await go();
// 1 select
await k('Alt+n'); await page.waitForTimeout(800); console.log('new focus', await focused(page));
await page.keyboard.type(`Incoterm ${suffix}`); await k('Tab'); await page.keyboard.type(`شروط التسليم ${suffix}`);
await k('Tab'); console.log('f', await focused(page)); await k('Tab'); console.log('f', await focused(page));
if (ar) await k('ArrowDown', 4); else await page.keyboard.type('Single'); await page.waitForTimeout(300); console.log('type', await focused(page));
await k('Tab'); console.log('f', await focused(page));
await page.keyboard.type('EXW | تسليم المصنع'); await k('Enter'); await page.keyboard.type('FOB | فوب'); await k('Enter'); await page.keyboard.type('CIF | سيف');
await k('Tab'); console.log('f', await focused(page)); await k('Tab'); console.log('f', await focused(page));
await page.keyboard.type('FOB'); await page.waitForTimeout(300); console.log('default', await focused(page));
await shot(page, `${tag}-new-select-field`);
await k('Control+s'); await page.waitForTimeout(2000); console.log('saved select', await msgs());
// 2 date with default
await k('Alt+n'); await page.waitForTimeout(800);
await page.keyboard.type(`Licence expiry ${suffix}`); await k('Tab'); await page.keyboard.type(`انتهاء الرخصة ${suffix}`);
await k('Tab'); await k('Tab'); if (ar) await k('ArrowDown', 2); else await page.keyboard.type('Date'); await page.waitForTimeout(300); console.log('type', await focused(page));
await k('Tab'); console.log('f', await focused(page)); await k('Tab'); console.log('f', await focused(page));
await page.keyboard.type(ar ? '٣١١٢٢٠٢٧' : '31122027'); console.log('date default', await focused(page));
await shot(page, `${tag}-new-date-field`);
await k('Control+s'); await page.waitForTimeout(2000); console.log('saved date', await msgs());
// 3 required number with default
await k('Alt+n'); await page.waitForTimeout(800);
await page.keyboard.type(`Credit cap ${suffix}`); await k('Tab'); await page.keyboard.type(`سقف الائتمان ${suffix}`);
await k('Tab'); await k('Tab'); if (ar) await k('ArrowDown', 1); else await page.keyboard.type('Number'); await page.waitForTimeout(300); console.log('type', await focused(page));
await k('Tab'); console.log('decimals', await focused(page)); await page.keyboard.press('Control+a'); await page.keyboard.type('2');
await k('Tab'); console.log('f', await focused(page)); await k('Space'); await k('Tab'); console.log('f', await focused(page));
await page.keyboard.type(ar ? '١٥٠٠٫٢٥' : '1500.25');
await k('Control+s'); await page.waitForTimeout(2000); console.log('saved number', await msgs());
await page.keyboard.press('Escape'); await page.waitForTimeout(1000);
await shot(page, `${tag}-fields-after-create`);
const res = await page.evaluate(async () => (await (await fetch('/api/customfields/fields?pageSize=100', { headers: { 'X-Erp-Request': '1' } })).json()));
console.log(JSON.stringify((res.items ?? res.rows ?? res).filter?.(r => /R4/.test(r.labelEn)).map(r => [r.key, r.labelEn, r.fieldType, r.required])));
for (const r of (res.items ?? []).filter(r => /R4/.test(r.labelEn))) { const f = await page.evaluate(async (id) => (await (await fetch('/api/customfields/fields/' + id, { headers: { 'X-Erp-Request': '1' } })).json()), r.id); console.log(f.key, f.type, 'default=', f.default, 'options=', JSON.stringify(f.options)); }
console.log('errors', errors); await browser.close();
