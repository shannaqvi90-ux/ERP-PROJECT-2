import { open, shot, focused } from './pw.mjs';
const { browser, page, errors } = await open('admin@alnoor.example');
const t = String(Date.now()).slice(-5);
await page.keyboard.press('Control+k'); await page.waitForTimeout(400); await page.keyboard.type('Custom fields'); await page.waitForTimeout(800); await page.keyboard.press('Enter'); await page.waitForTimeout(2000);
// Date field with a default, typed dd/mm/yyyy
await page.keyboard.press('Alt+n'); await page.waitForTimeout(800);
await page.keyboard.type('Licence expiry ' + t); await page.keyboard.press('Tab');
await page.keyboard.type('انتهاء الرخصة ' + t); await page.keyboard.press('Tab');
console.log('1', await focused(page)); await page.keyboard.press('Tab');
console.log('2', await focused(page)); await page.keyboard.type('Date'); await page.waitForTimeout(400);
for (let i = 0; i < 4; i++) { await page.keyboard.press('Tab'); console.log('tab', i, await focused(page)); const f = await focused(page); if (f.includes('defaultValue')) break; }
await page.keyboard.type('31/12/2027'); await page.waitForTimeout(300);
const v = await page.evaluate(() => { const e = document.activeElement; return [e.value, e.type, e.placeholder]; });
console.log('date default input after typing 31/12/2027:', v);
await shot(page, 'tmp-date-default');
await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
await shot(page, 'tmp-date-saved');
const msg = await page.$$eval('[role=alert], .field-error, .toast, [role=status]', els => els.map(e => e.textContent.trim()).filter(Boolean));
console.log('messages', msg);
console.log('errors', errors);
await browser.close();
