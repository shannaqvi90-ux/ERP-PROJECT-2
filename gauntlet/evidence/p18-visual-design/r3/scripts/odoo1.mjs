// odoo1: the reference's contacts list (100,000+) and a contact form: cut text with no title, resize handles; blind screenshots
import { browser, OUT } from './lib.mjs';
const b = await browser();
const page = await (await b.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
await page.goto('http://localhost:8069/web/login');
await page.fill('input[name=login]', 'admin'); await page.fill('input[name=password]', 'admin'); await page.keyboard.press('Enter');
await page.waitForTimeout(4000);
await page.goto('http://localhost:8069/odoo/contacts?view_type=list'); await page.waitForTimeout(6000);
const hideBrand = () => page.addStyleTag({ content: '.o_main_navbar .o_menu_brand, .o_navbar_apps_menu, img[src*=logo], .o_user_menu img { visibility: hidden !important }' });
await hideBrand();
const m = await page.evaluate(() => {
  const cells = [...document.querySelectorAll('tbody td')].filter(c => { const r = c.getBoundingClientRect(); return r.width && r.top < innerHeight; });
  const cut = cells.filter(c => c.scrollWidth > c.clientWidth + 1 || [...c.querySelectorAll('*')].some(e => e.children.length === 0 && e.scrollWidth > e.clientWidth + 1));
  const untitled = cut.filter(c => !c.title && !c.querySelector('[title]') && !c.closest('[title]'));
  return { rows: document.querySelectorAll('tbody tr.o_data_row').length, cells: cells.length, cut: cut.length, untitled: untitled.length, sample: untitled.slice(0, 5).map(c => c.textContent.trim().slice(0, 40)), resize: document.querySelectorAll('.o_resize').length, headers: [...document.querySelectorAll('thead th')].map(t => t.textContent.trim()).filter(Boolean), pager: document.querySelector('.o_pager')?.textContent.trim() };
});
console.log(JSON.stringify(m));
await page.screenshot({ path: `${OUT}/11-reference-contacts-list.jpg`, type: 'jpeg', quality: 70 });
await page.locator('tbody tr.o_data_row').nth(3).click(); await page.waitForTimeout(3000);
await hideBrand();
const f = await page.evaluate(() => { const els = [...document.querySelectorAll('.o_form_view span, .o_form_view input, .o_form_view label, .o_form_view a, .o_form_view button')].filter(e => { const r = e.getBoundingClientRect(); return r.width && r.top < innerHeight; }); const cut = els.filter(e => e.scrollWidth > e.clientWidth + 1 && (e.tagName === 'INPUT' || e.children.length === 0) && getComputedStyle(e).overflow !== 'visible'); return { cut: cut.length, untitled: cut.filter(e => !e.title && !e.closest('[title]')).map(e => (e.value || e.textContent).trim().slice(0, 40)).slice(0, 8) }; });
console.log('form', JSON.stringify(f));
await page.screenshot({ path: `${OUT}/12-reference-contact-form.jpg`, type: 'jpeg', quality: 70 });
await b.close();
