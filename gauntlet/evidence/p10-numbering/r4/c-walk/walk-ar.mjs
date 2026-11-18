import { open, shot } from './lib.mjs';
const { browser, page, errors } = await open('admin.ar@alnoor.example', { locale: 'ar-AE' });
console.log('dir', await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang));
await page.keyboard.press('g'); await page.keyboard.press('n');
await page.waitForURL(/numbering/); await page.waitForTimeout(1200);
await shot(page, 'c05-ar-list');
await page.keyboard.type('SO-');
await page.waitForTimeout(1200);
console.log('rows', await page.locator('table tbody tr').count(), (await page.locator('table tbody tr').first().innerText()).replace(/\s+/g,' '));
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
await page.waitForSelector('input[name=prefix]'); await page.waitForTimeout(500);
const sel = page.locator('select').first();
await sel.focus(); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); // Never
await page.waitForTimeout(500);
console.log('preview never:', (await page.locator('.nb-preview').innerText()).replace(/\s+/g,' '));
await sel.focus(); await page.keyboard.press('ArrowUp'); await page.keyboard.press('ArrowUp'); // yearly
await page.locator('input[name=prefix]').focus(); await page.keyboard.press('Control+a'); await page.keyboard.type('SO-{YY}-');
await page.waitForTimeout(500);
console.log('preview yearly:', (await page.locator('.nb-preview').innerText()).replace(/\s+/g,' '));
await shot(page, 'c06-ar-yearly-preview');
await sel.focus(); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); // never
await page.keyboard.press('Control+s'); await page.waitForTimeout(2500);
const main = (await page.locator('main').innerText()).replace(/\s+/g,' ');
console.log('after save:', main.slice(main.indexOf('SO'), main.indexOf('SO') + 600));
await shot(page, 'c07-ar-saved-change-line');
// open History
const hist = page.getByRole('button', { name: /السجل|History|التاريخ/ }).first();
console.log('history buttons', await page.getByRole('button').allInnerTexts());
await hist.click().catch(e => console.log('no history btn', e.message));
await page.waitForTimeout(2000);
const dlg = await page.locator('[role=dialog], aside, .history').last().innerText().catch(() => '');
console.log('history text:', dlg.replace(/\s+/g, ' ').slice(0, 1200));
await shot(page, 'c08-ar-history');
console.log('errors', errors);
await browser.close();
