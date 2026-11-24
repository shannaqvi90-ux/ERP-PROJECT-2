// An old contact, unchanged, saved from its form while a required yes/no field with default Yes exists.
import { open, shot, text, focused } from './pw.mjs';
const email = process.argv[2] || 'admin@alnoor.example', tag = process.argv[3] || 'en', who = process.argv[4] || 'Emirates Cables';
const { browser, page, errors } = await open(email);
const msgs = () => page.$$eval('[role=alert], [role=status], .field-error', els => els.map(e => e.textContent.trim()).filter(Boolean).slice(0, 6));
await page.keyboard.press('g'); await page.keyboard.press('o'); await page.waitForTimeout(2500);
await page.keyboard.press('/'); await page.keyboard.type(who); await page.waitForTimeout(2000);
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(3000);
console.log('bool shows', await page.locator('[name="customFields.cfKeyAccountR4"]').inputValue());
await page.keyboard.press('Control+s'); await page.waitForTimeout(2500); console.log('save unchanged', await msgs());
await page.focus('[name="customFields.cfKeyAccountR4"]');
await shot(page, `${tag}-old-contact-required-yes-no-default`);
console.log('errors', errors); await browser.close();
