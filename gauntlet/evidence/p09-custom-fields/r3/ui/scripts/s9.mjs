import { open, shot, focused } from './pw.mjs';
const { browser, page, errors } = await open('admin.ar@alnoor.example');
await page.keyboard.press('g'); await page.keyboard.press('o'); await page.waitForTimeout(2500);
await page.keyboard.press('/'); await page.keyboard.type('Gulf Steel'); await page.waitForTimeout(2000);
await page.keyboard.press('ArrowDown'); await page.waitForTimeout(300); await page.keyboard.press('Enter'); await page.waitForTimeout(3000);
await page.focus('select[name="customFields.cfIncoterm55484"]');
await page.keyboard.press('Alt+Shift+f'); await page.waitForTimeout(3000);
await shot(page, 's24-ar-filtered-by-custom-select');
const t = await page.evaluate(() => document.body.innerText);
console.log(t.split('\n').filter(l => /55484|جهة اتصال|جهتا|تسليم/.test(l) && l.length < 120).slice(0, 12).join('\n'));
// column chooser: Alt+? use the Columns button by keyboard
const cols = page.getByRole('button', { name: 'الأعمدة' }).first(); await cols.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
const t2 = await page.evaluate(() => document.body.innerText);
console.log('chooser lists custom:', /شروط التسليم 55484/.test(t2), /شريحة العميل/.test(t2));
await shot(page, 's24-ar-column-chooser');
console.log('errors', errors);
await browser.close();
