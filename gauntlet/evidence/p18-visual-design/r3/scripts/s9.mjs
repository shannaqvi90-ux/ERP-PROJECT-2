// s9: keyboard reach on a record: section jump buttons, date calendar by Alt+ArrowDown, Country search drop-down by keyboard, a toast after save
import { browser, signIn, shot, BASE } from './lib.mjs';
const b = await browser();
const page = await (await b.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
const lang = process.env.LANG2 || 'en';
await signIn(page, lang === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example');
await page.goto(BASE + '/contacts/contacts'); await page.waitForTimeout(2500);
await page.locator('tbody tr').nth(5).click(); await page.keyboard.press('Enter'); await page.waitForTimeout(2000);
console.log(JSON.stringify(await page.evaluate(() => ({
  sectionButtons: [...document.querySelectorAll('.section-tabs button')].map(b => b.textContent.trim() + ':' + b.tabIndex),
  dateBtn: [...document.querySelectorAll('.date-picker-button')].map(b => b.tabIndex),
  combos: [...document.querySelectorAll('[role=combobox]')].map(c => (c.getAttribute('aria-label') || c.name || c.id) + ':' + c.tagName),
}))));
// Tab walk: list every focus stop in the record pane
const stops = [];
await page.locator('main input').first().focus();
for (let i = 0; i < 45; i++) { await page.keyboard.press('Tab'); stops.push(await page.evaluate(() => { const e = document.activeElement; return (e.getAttribute('aria-label') || e.name || e.textContent || '').trim().slice(0, 24) + '<' + e.tagName.toLowerCase() + '>'; })); }
console.log('tab stops', stops.join(' | '));
// Calendar by keyboard
const d = page.locator('main input[name="customFields.cfCreditReview"]');
await d.focus(); await page.keyboard.press('Alt+ArrowDown'); await page.waitForTimeout(500);
const cal = await page.evaluate(() => { const c = document.querySelector('.calendar-pop'); return c ? { text: c.innerText.slice(0, 120).replace(/\n+/g, ' '), focus: document.activeElement?.getAttribute('aria-label') || document.activeElement?.textContent } : null; });
console.log('calendar', JSON.stringify(cal));
if (cal) { await page.keyboard.press('ArrowRight'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(300); console.log('picked', await d.inputValue()); }
await shot(page, `x-keyboard-${lang}`);
// Country: a drop-down with search, by keyboard
const country = page.locator('main [role=combobox]').filter({ hasNot: page.locator('xx') });
console.log('combobox count', await country.count());
await b.close();
