import { open, shot, BASE } from './lib.mjs';
const { browser, page, errors } = await open('admin@alnoor.example');
const log = (...a) => console.log(...a);
await page.keyboard.press('g'); await page.keyboard.press('o'); await page.waitForTimeout(2500);
await page.getByRole('button', { name: 'Columns' }).focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(800);
for (const c of ['Insurance cover 31', 'Licence expiry 31', 'Incoterm 14727', 'Parent account 31', 'VAT verified 31', 'Trade licence no 31']) { await page.getByLabel('Show ' + c).focus(); await page.keyboard.press('Space'); }
await page.getByRole('button', { name: 'Done' }).focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
// header menu on Insurance cover
const hdrBtns = await page.locator('[role=columnheader] button').evaluateAll(bs => bs.map(b => (b.getAttribute('aria-label') || b.innerText).slice(0, 60)));
log('header buttons', hdrBtns.join(' | '));
const menuBtn = page.locator('[role=columnheader]', { hasText: 'Insurance cover 31' }).locator('button').last();
await menuBtn.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(800);
await shot(page, 'x-en-header-menu');
log('menu:', (await page.locator('[role=menu]').last().innerText().catch(() => 'none')).slice(0, 600));
log(errors);
await browser.close();
