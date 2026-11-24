// Lookup picking by typing, and Alt+Shift+F on a custom select, by keyboard.
import { open, shot, text, focused } from './pw.mjs';
const email = process.argv[2] || 'admin@alnoor.example', tag = process.argv[3] || 'en', sfx = process.argv[4] || 'R4E';
const ar = tag === 'ar';
const { browser, page, errors } = await open(email);
await page.keyboard.press('g'); await page.keyboard.press('o'); await page.waitForTimeout(2500);
await page.keyboard.press('/'); await page.keyboard.type(process.env.WHO || 'Gulf Glass'); await page.waitForTimeout(2000);
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(3000);
const look = page.locator('[name="customFields.cfParentCompanyR4"]').first();
console.log('lookup element', await look.evaluate(e => e.outerHTML.slice(0, 300)).catch(e => 'none ' + e.message));
await look.focus(); await page.keyboard.type('Gulf Glass'); await page.waitForTimeout(2500);
const opts = await page.$$eval('[role=option]', els => els.map(e => e.innerText.replace(/\s+/g, ' ').trim()).slice(0, 12));
console.log('options', JSON.stringify(opts, null, 0));
await shot(page, `${tag}-lookup-picking`);
await page.keyboard.press('Escape');
// Alt+Shift+F on the Incoterm select (value CIF from u2)
await page.reload(); await page.waitForTimeout(3000);
await page.focus(`select[name="customFields.cfIncoterm${sfx}"]`);
await page.keyboard.press('Alt+Shift+f'); await page.waitForTimeout(3000);
console.log('url', decodeURIComponent(page.url()));
const t = await text(page); console.log(t.split('\n').filter(l => /Incoterm|شروط|contacts?$|جهة اتصال|جهات اتصال|^\d+ /.test(l) && l.length < 120).slice(0, 10).join(' | '));
await shot(page, `${tag}-filtered-by-custom-select`);
console.log('errors', errors); await browser.close();
