import { open, shot, focused } from './pw.mjs';
const { browser, page, errors } = await open('admin.ar@alnoor.example');
await page.keyboard.press('g'); await page.keyboard.press('o'); await page.waitForTimeout(2500);
await page.keyboard.press('/'); await page.keyboard.type('Gulf Steel'); await page.waitForTimeout(2000);
await page.keyboard.press('ArrowDown'); await page.waitForTimeout(300); await page.keyboard.press('Enter'); await page.waitForTimeout(3000);
console.log(page.url());
await page.focus('input[name="customFields.cfLicenceExpiry55484"]'); await page.keyboard.type('٣١١٢٢٠٢٧');
console.log('date typed ->', await page.evaluate(() => document.activeElement.value));
await page.focus('select[name="customFields.cfIncoterm55484"]'); await page.keyboard.press('ArrowDown'); // first option FOB
console.log('select ->', await page.evaluate(() => document.activeElement.value));
await page.keyboard.press('Control+s'); await page.waitForTimeout(2000);
let msg = await page.$$eval('[role=alert], .field-error', els => els.map(e => e.textContent.trim()).filter(Boolean)); console.log('save 1', msg);
await page.focus('select[name="customFields.cfIncoterm55484"]'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown');
console.log('select ->', await page.evaluate(() => document.activeElement.value));
await page.keyboard.press('Control+s'); await page.waitForTimeout(2000);
msg = await page.$$eval('[role=alert], .field-error', els => els.map(e => e.textContent.trim()).filter(Boolean)); console.log('save 2', msg);
// also change the seeded select Customer segment (value keys differ from labels)
await page.focus('select[name="customFields.cfCustomerSegment"]'); const before = await page.evaluate(() => document.activeElement.value); await page.keyboard.press('ArrowDown');
console.log('segment', before, '->', await page.evaluate(() => document.activeElement.value));
await page.keyboard.press('Control+s'); await page.waitForTimeout(2000);
const hist = page.getByRole('button', { name: /السجل|سجل التغييرات|التاريخ/ }).first(); console.log('hist btn', await hist.count());
await hist.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(2500);
await shot(page, 's24-ar-history-select-values');
const text = await page.evaluate(() => document.body.innerText);
console.log(text.split('\n').filter(l => /55484|شريحة|Segment/.test(l) && l.length < 160).slice(0, 20).join('\n'));
console.log('errors', errors);
await browser.close();
