// s5: contact form (new contact) components by keyboard: search drop-downs, date typed with slashes, money field, tag field, toasts;
// company form; then Arabic screens and the three themes; theme per user across sign-out
import { browser, signIn, shot, CUT, BASE } from './lib.mjs';
const b = await browser();
const ctx = await b.newContext({ viewport: { width: 1440, height: 900 } });
const page = await ctx.newPage();
const errors = []; page.on('pageerror', e => errors.push(String(e))); page.on('response', r => { if (r.status() >= 500) errors.push(r.status() + ' ' + r.url()); });
await signIn(page, 'admin@alnoor.example');
await page.goto(BASE + '/contacts/contacts'); await page.waitForTimeout(2500);
await page.keyboard.press('Alt+n'); await page.waitForTimeout(1500);
const form = await page.evaluate(() => ({
  url: location.pathname,
  selects: [...document.querySelectorAll('main select')].filter(s => s.offsetParent && !s.classList.contains('search-select-native')).map(s => s.name),
  combos: [...document.querySelectorAll('main [role=combobox]')].map(c => c.getAttribute('aria-label') || c.id || c.textContent.trim().slice(0, 20)),
  dates: [...document.querySelectorAll('main input')].filter(i => /dd\/mm|يوم/.test(i.placeholder)).map(i => i.name),
  tabs: [...document.querySelectorAll('[role=tab]')].map(t => t.textContent.trim() + (t.tabIndex < 0 ? '' : '[tab]')),
  labels: [...document.querySelectorAll('main label > span:first-child')].map(s => s.textContent.trim()).slice(0, 60),
}));
console.log(JSON.stringify(form));
await shot(page, '06-new-contact-en-dark');
// Emirate / Country drop-down with search by keyboard
for (const name of ['country', 'emirate']) {
  const c = page.locator(`main [role=combobox][aria-label*="${name === 'country' ? 'Country' : 'Emirate'}"], main [name=${name}] ~ [role=combobox]`).first();
  console.log(name, 'combobox found', await c.count());
}
// Date typed with slashes (routed bug from p09's critic): find any dd/mm/yyyy input in the form
const dateName = form.dates[0];
if (dateName) {
  const d = page.locator(`main input[name="${dateName}"]`);
  await d.focus(); await page.keyboard.type('15/06/2027'); await page.waitForTimeout(300);
  console.log('date typed 15/06/2027 ->', await d.inputValue());
  await d.fill(''); await d.focus(); await page.keyboard.type('2027-06-15'); await page.waitForTimeout(300);
  console.log('date typed 2027-06-15 ->', await d.inputValue());
}
console.log('errors', JSON.stringify(errors));
await b.close();
