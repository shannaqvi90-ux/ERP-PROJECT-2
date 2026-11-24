// With a yes/no custom field whose default is Yes: a new contact from the form, by keyboard.
import { open, shot, text, focused } from './pw.mjs';
const email = process.argv[2] || 'admin@alnoor.example', tag = process.argv[3] || 'en';
const { browser, page, errors } = await open(email);
const msgs = () => page.$$eval('[role=alert], [role=status], .field-error', els => els.map(e => e.textContent.trim()).filter(Boolean).slice(0, 6));
const reqs = []; page.on('response', async r => { if (/\/api\/contacts\/contacts$/.test(r.url()) && r.request().method() === 'POST') reqs.push(r.status() + ' ' + r.request().postData()?.match(/"customFields":\{[^}]*\}/)?.[0] + ' -> ' + (await r.text()).slice(0, 200)); });
await page.keyboard.press('g'); await page.keyboard.press('o'); await page.waitForTimeout(2500);
await page.keyboard.press('Alt+n'); await page.waitForTimeout(1500); console.log('focus', await focused(page));
await page.keyboard.type('Bool default form ' + Date.now());
const b = page.locator('[name="customFields.cfKeyAccountR4"]'); console.log('bool shows', await b.inputValue().catch(e => 'n/a'));
await page.keyboard.press('Control+s'); await page.waitForTimeout(2500); console.log('save', await msgs());
console.log(reqs.join('\n'));
await shot(page, `${tag}-new-contact-yes-no-default`);
console.log('errors', errors); await browser.close();
