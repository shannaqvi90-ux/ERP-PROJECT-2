// Add a 2-decimal number field and a select field by keyboard only.
import { open, log, focused, shot } from './common.mjs';
const { browser, page } = await open('en');
await page.goto('http://localhost:20950/customfields/fields'); await page.locator('main h1').waitFor(); await page.waitForTimeout(1000);
const t0 = Date.now();
if (!process.env.SKIPNUM) {
await page.keyboard.press('Alt+n'); await page.waitForTimeout(800);
await page.keyboard.type('Insurance cover'); await page.keyboard.press('Tab');
await page.keyboard.type('التغطية التأمينية'); await page.keyboard.press('Tab'); await page.keyboard.press('Tab');
await page.keyboard.type('Number'); await page.waitForTimeout(400);
log('type focused', await focused(page), await page.evaluate(() => document.activeElement.value));
// what comes after type now?
const order = [];
for (let i = 0; i < 6; i++) { await page.keyboard.press('Tab'); order.push(await focused(page)); }
log('after type', order.join(' | '));
const dec = page.getByLabel('Decimals (0 to 8)');
log('decimals field', await dec.count(), await dec.inputValue().catch(() => '-'));
await dec.focus(); await page.keyboard.press('Control+a'); await page.keyboard.type('2');
await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
log('after save', (await page.locator('main').innerText()).replace(/\n+/g, ' / ').slice(0, 400), (Date.now() - t0) + ' ms');
await shot(page, 'en-number-field-saved');
}
// Select field with options and default by label
await page.keyboard.press('Alt+n'); await page.waitForTimeout(800);
await page.keyboard.type('Incoterm'); await page.keyboard.press('Tab');
await page.keyboard.type('شروط التسليم'); await page.keyboard.press('Tab'); await page.keyboard.press('Tab');
await page.keyboard.type('Single'); await page.waitForTimeout(400);
const opts = page.locator('textarea[name=options]');
log('options field', await opts.count());
await opts.focus(); await page.keyboard.type('EXW | تسليم المصنع'); await page.keyboard.press('Enter'); await page.keyboard.type('FOB | التسليم على ظهر السفينة'); await page.keyboard.press('Enter'); await page.keyboard.type('CIF | التكلفة والتأمين والشحن');
const def = page.getByLabel('Default for new records');
await def.focus(); log('default focused', await focused(page));
const tag = await def.evaluate(e => e.tagName);
if (tag === 'SELECT') await def.selectOption({ label: 'FOB' }); else await page.keyboard.type('FOB');
await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
log('after select save', (await page.locator('main').innerText()).replace(/\n+/g, ' / ').slice(0, 600));
await shot(page, 'en-select-field-saved');
await browser.close();
