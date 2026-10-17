import { api, browser, signIn, BASE, OUT, log } from './lib.mjs';
const a = await api('admin.ar@alnoor.example');
const list = await a.call('GET', '/api/tenancy/companies?take=50');
const co = list.body.items.find(c => c.code === 'ALN-DXB');
const rec0 = (await a.call('GET', '/api/tenancy/companies/' + co.id)).body;
const b = await browser();
const { page } = await signIn(b, 'admin.ar@alnoor.example');
log('dir', await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang));
await page.goto(`${BASE}/tenancy/companies/${co.id}`);
const PHONE = '[data-field="phone"] input';
await page.locator(PHONE).waitFor(); await page.waitForTimeout(600);
const ph = page.locator(PHONE);
log('phone input dir', await ph.evaluate(e => [e.dir, getComputedStyle(e).direction, e.inputMode, e.type, e.getAttribute('aria-describedby')]));
// Arabic-Indic digits
await ph.click(); await page.keyboard.type('٠٥٠٥٥٥٠١٩٩'); await page.waitForTimeout(1500);
const s1 = (await a.call('GET', '/api/tenancy/companies/' + co.id)).body.phone;
log('arabic-indic digits typed -> shown', JSON.stringify(await ph.inputValue()), 'stored', JSON.stringify(s1), 'notice', JSON.stringify(await page.locator('.record-form .notice').textContent().catch(() => null)));
await page.screenshot({ path: OUT + '/form-ar-phone-autosaved.png' });
await page.keyboard.press('Alt+z'); await page.waitForTimeout(1200);
log('after Alt+Z stored', JSON.stringify((await a.call('GET', '/api/tenancy/companies/' + co.id)).body.phone));
// Country field
const country = page.locator('[data-field="country"]');
log('country field html', (await country.innerHTML()).slice(0, 600));
// field types present in form
const types = await page.$$eval('.record-form [data-field]', els => els.map(e => e.getAttribute('data-field') + ':' + (e.querySelector('input,select,textarea')?.tagName || '') + ':' + (e.querySelector('input')?.type || '') + (e.querySelector('[disabled]') ? ':disabled' : '')));
log('fields', types.join(' '));
// dirty + Escape leave guard in Arabic
const email = page.locator('[data-field="email"] input');
await email.click(); await page.keyboard.press('End'); await page.keyboard.type('z');
log('header dirty', JSON.stringify(await page.locator('.record-header').textContent()));
await page.keyboard.press('Escape'); await page.waitForTimeout(500);
const dlg = page.locator('[role="dialog"], [role="alertdialog"]');
log('dialog after Escape', await dlg.count(), JSON.stringify((await dlg.first().textContent().catch(() => '')).slice(0, 200)));
await page.screenshot({ path: OUT + '/form-ar-leave-dialog.png' });
await page.keyboard.press('Escape'); await page.waitForTimeout(300);
// server validation: bad email, Ctrl+S
await email.fill('not-an-email'); await page.keyboard.press('Control+s'); await page.waitForTimeout(1200);
log('server error', JSON.stringify(await page.locator('[data-field="email"]').textContent()), 'focused', await page.evaluate(() => document.activeElement?.closest('[data-field]')?.getAttribute('data-field')));
await page.screenshot({ path: OUT + '/form-ar-server-error.png' });
await page.keyboard.press('Alt+z'); await page.waitForTimeout(300);
log('after discard header', JSON.stringify(await page.locator('.record-header').textContent()));
// next/previous
const url0 = page.url(); await page.keyboard.press('Alt+PageDown'); await page.waitForTimeout(1000); log('Alt+PageDown moved', url0 !== page.url(), page.url());
await page.keyboard.press('Alt+PageUp'); await page.waitForTimeout(1000); log('Alt+PageUp back', page.url() === url0);
const now = (await a.call('GET', '/api/tenancy/companies/' + co.id)).body;
if (now.phone !== rec0.phone || now.email !== rec0.email) { await a.call('PUT', '/api/tenancy/companies/' + co.id, { ...now, phone: rec0.phone, email: rec0.email }); log('restored'); }
await b.close();
