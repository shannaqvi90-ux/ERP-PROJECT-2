import { open, shot } from './lib.mjs';
const { browser, page, errors } = await open('admin@alnoor.example');
const puts = [];
page.on('response', r => { if (r.request().method() === 'PUT') puts.push(`${r.status()} ${r.url()}`); });
let keys = 0; const press = async k => { keys++; await page.keyboard.press(k); }; const type = async s => { keys += s.length; await page.keyboard.type(s); };
await press('g'); await press('n');
await page.waitForURL(/numbering/); await page.waitForTimeout(800);
await type('quotation dxb'); await page.waitForTimeout(1200);
await press('ArrowDown'); await press('Enter');
await page.waitForSelector('input[name=prefix]'); await page.waitForTimeout(500);
// prefix focused: replace with a monthly pattern
await press('Control+a'); await type('QT-{YY}{MM}-');
// to the Starts again select
await page.locator('select').first().focus();
const sel = page.locator('select').first();
console.log('select options', await sel.locator('option').allInnerTexts());
await press('ArrowDown'); // Every month
await page.waitForTimeout(600);
console.log('preview monthly:', (await page.locator('.nb-preview').innerText()).replace(/\s+/g,' '));
await shot(page, 'c02-en-monthly-preview');
// back to yearly and a year-only pattern, digits 6
await page.locator('input[name=prefix]').focus(); await press('Control+a'); await type('QT/{YYYY}/');
await page.locator('input[name=padding]').focus(); await press('Control+a'); await type('6');
await sel.focus(); await press('ArrowUp');
await page.waitForTimeout(600);
console.log('preview yearly:', (await page.locator('.nb-preview').innerText()).replace(/\s+/g,' '));
await shot(page, 'c03-en-yearly-preview');
await press('Control+s');
await page.waitForTimeout(2500);
console.log('PUTs', puts, 'keys so far', keys);
console.log('after save:', (await page.locator('main').innerText()).replace(/\s+/g,' ').slice(0, 900));
await shot(page, 'c04-en-saved');
console.log('errors', errors);
await browser.close();
