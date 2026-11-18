import { open, shot, BASE } from './lib.mjs';
const { browser, page, errors } = await open('admin@alnoor.example');
const log = (...a) => console.log(...a);
const focusDesc = () => page.evaluate(() => { const e = document.activeElement; return e ? `${e.tagName} name=${e.getAttribute('name')} type=${e.getAttribute('type')} label=${e.labels?.[0]?.innerText ?? ''}` : 'none'; });
const stamp = Date.now() % 100000;
await page.goto(BASE + '/customfields/fields'); await page.waitForTimeout(2000);
// 1. Single select with a default chosen in the same save (r2 gap)
await page.keyboard.press('Alt+n'); await page.waitForTimeout(800);
await page.keyboard.type('Incoterm ' + stamp); await page.keyboard.press('Tab');
await page.keyboard.type('شروط التسليم ' + stamp); await page.keyboard.press('Tab');
log('at', await focusDesc()); await page.keyboard.press('Tab');
log('at', await focusDesc()); await page.keyboard.type('Single');
await page.waitForTimeout(300); await page.keyboard.press('Tab');
log('at', await focusDesc());
await page.keyboard.type('EXW | تسليم المصنع'); await page.keyboard.press('Enter');
await page.keyboard.type('FOB | تسليم على ظهر السفينة'); await page.keyboard.press('Enter');
await page.keyboard.type('CIF | التكلفة والتأمين والشحن');
for (let i = 0; i < 4; i++) { await page.keyboard.press('Tab'); log('at', await focusDesc()); if ((await focusDesc()).includes('Default')) break; }
await page.keyboard.type('FOB'); await page.waitForTimeout(300);
log('default select value:', await page.evaluate(() => document.activeElement.value));
await shot(page, 'x-en-select-before-save');
await page.keyboard.press('Control+s'); await page.waitForTimeout(2000);
await shot(page, 'x-en-select-after-save');
log('main:', (await page.locator('main').innerText()).slice(-900));
log(errors);
await browser.close();
