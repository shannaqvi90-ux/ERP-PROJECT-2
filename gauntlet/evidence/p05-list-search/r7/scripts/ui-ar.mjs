import { open, shot } from './pw.mjs';
const base = process.argv[2], out = process.argv[3];
const { browser, page } = await open(base, 'admin.ar@alnoor.example', { width: 1600, height: 900, locale: 'ar-AE' });
console.log('dir:', await page.evaluate(() => document.documentElement.dir), 'lang:', await page.evaluate(() => document.documentElement.lang));
const nav = page.getByRole('navigation').getByRole('link', { name: 'المستخدمون' }).first();
await nav.click();
await page.getByRole('row').nth(5).waitFor();
const body = () => page.locator('main').innerText().catch(() => page.locator('body').innerText());
console.log('header/count:', (await body()).split('\n').slice(0, 6).join(' | '));
await shot(page, out + '/08-users-list-ar.jpg');
await page.keyboard.type('ماجد انيل بيلاى');
await page.waitForTimeout(1500);
const rows = await page.getByRole('row').allInnerTexts();
console.log('variant search first rows:', rows.slice(1, 3).map(r => r.replace(/\s+/g, ' ')).join(' || '));
console.log('count text:', (await body()).split('\n').slice(0, 5).join(' | '));
await shot(page, out + '/09-arabic-variant-search.jpg');
// bulk confirm dialog in Arabic: select all matching then Deactivate -> cancel
await page.getByRole('searchbox').or(page.getByLabel(/بحث/)).first().fill('خالد');
await page.waitForTimeout(1500);
await page.getByRole('grid').focus().catch(() => {});
await page.keyboard.press('ArrowDown').catch(() => {});
await page.keyboard.press('Control+a'); await page.waitForTimeout(300); await page.keyboard.press('Control+a'); await page.waitForTimeout(600);
const btns = await page.getByRole('button').allInnerTexts();
console.log('buttons:', btns.filter(b => b.trim()).slice(0, 25).join(' | '));
const deact = page.getByRole('button', { name: /إلغاء التفعيل|تعطيل|إيقاف/ }).first();
if (await deact.count()) { await deact.click(); await page.waitForTimeout(600); console.log('confirm dialog:', (await page.locator('[role="dialog"], [role="alertdialog"]').first().innerText().catch(() => 'none')).replace(/\s+/g, ' ')); await shot(page, out + '/10-bulk-confirm-ar.jpg'); await page.keyboard.press('Escape'); }
await browser.close();
