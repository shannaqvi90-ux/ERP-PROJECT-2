// A date field with a default typed as the placeholder shows it, then as bare digits.
import { open, shot, text, focused } from './pw.mjs';
const email = process.argv[2] || 'admin@alnoor.example', tag = process.argv[3] || 'en', suffix = process.argv[4] || 'R4';
const ar = tag === 'ar';
const { browser, page, errors } = await open(email);
const msgs = () => page.$$eval('[role=alert], [role=status], .field-error, [aria-live]', els => els.map(e => e.textContent.trim()).filter(Boolean).slice(0, 6));
await page.keyboard.press('Control+k'); await page.keyboard.type(ar ? 'الحقول المخصصة' : 'Custom fields'); await page.waitForTimeout(1000); await page.keyboard.press('Enter'); await page.waitForTimeout(2000);
await page.keyboard.press('Alt+n'); await page.waitForTimeout(800);
await page.keyboard.type(`Licence expiry ${suffix}`); await page.keyboard.press('Tab'); await page.keyboard.type(`انتهاء الرخصة ${suffix}`);
await page.keyboard.press('Tab'); await page.keyboard.press('Tab'); if (ar) { await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); } else await page.keyboard.type('Date');
await page.keyboard.press('Tab'); await page.keyboard.press('Tab');
console.log('placeholder', await page.getAttribute('input[name=defaultValue]', 'placeholder'));
for (const typed of (ar ? ['٣١/١٢/٢٠٢٧', '٣١١٢٢٠٢٧'] : ['31/12/2027', '2027-12-31', '31122027'])) {
  await page.focus('input[name=defaultValue]'); await page.keyboard.press('Control+a'); await page.keyboard.press('Backspace');
  await page.keyboard.type(typed); console.log('typed', typed, '->', await page.inputValue('input[name=defaultValue]'));
}
await page.keyboard.press('Control+s'); await page.waitForTimeout(2000); console.log('saved', await msgs());
await shot(page, `${tag}-date-default-digits`);
const res = await page.evaluate(async () => (await (await fetch('/api/customfields/fields?pageSize=100', { headers: { 'X-Erp-Request': '1' } })).json()));
for (const r of (res.items ?? []).filter(r => r.labelEn.startsWith('Licence expiry'))) { const f = await page.evaluate(async (id) => (await (await fetch('/api/customfields/fields/' + id, { headers: { 'X-Erp-Request': '1' } })).json()), r.id); console.log(f.key, f.type, 'default=', f.default); }
console.log('errors', errors); await browser.close();
