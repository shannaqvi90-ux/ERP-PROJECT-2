import { open, shot, focused } from './pw.mjs';
const email = process.argv[2] || 'admin@alnoor.example', tag = process.argv[3] || 'en';
const { browser, page, errors } = await open(email);
await page.keyboard.press('g'); await page.keyboard.press('o'); await page.waitForTimeout(2500);
await page.keyboard.press('/'); await page.keyboard.type('Gulf Glass'); await page.waitForTimeout(1500);
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(2500);
console.log(page.url());
await shot(page, `tmp-${tag}-contact-form`);
const fields = await page.$$eval('[data-field], input[name^="customFields"], select[name^="customFields"]', els => els.map(e => (e.getAttribute('data-field') || e.getAttribute('name'))).filter(n => /cf|custom/i.test(n)));
console.log('custom inputs', fields);
// Try to save the existing contact without touching anything new: change nothing but a harmless note? press Ctrl+S
await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
const msg = await page.$$eval('[role=alert], .field-error, [role=status]', els => els.map(e => e.textContent.trim()).filter(Boolean));
console.log('after Ctrl+S', msg);
console.log('errors', errors);
await browser.close();
