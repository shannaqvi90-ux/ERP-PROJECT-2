import { api, browser, signIn, BASE, OUT, log } from './lib.mjs';
const a = await api('admin@alnoor.example');
const list = await a.call('GET', '/api/tenancy/companies?take=50');
const co = list.body.items.find(c => c.code === 'ALN-DXB');
const rec0 = (await a.call('GET', '/api/tenancy/companies/' + co.id)).body;
log('start phone', rec0.phone, 'version', rec0.version);
const b = await browser();
const { page } = await signIn(b, 'admin@alnoor.example');
const PHONE = '[data-field="phone"] input';
const status = () => page.locator('.record-form .notice').textContent().catch(() => null);
const stored = async () => (await a.call('GET', '/api/tenancy/companies/' + co.id)).body;
async function open() { await page.goto(`${BASE}/tenancy/companies/${co.id}`); await page.locator(PHONE).waitFor(); await page.waitForTimeout(500); }
// 1: 9 digits only after click
await open();
await page.locator(PHONE).click();
log('1 selection after click', JSON.stringify(await page.locator(PHONE).evaluate(e => [e.selectionStart, e.selectionEnd, e.value])));
await page.keyboard.type('505550199');
await page.waitForTimeout(1500);
log('1 typed 505550199 -> shown', JSON.stringify(await page.locator(PHONE).inputValue()), 'notice', JSON.stringify(await status()), 'stored', JSON.stringify((await stored()).phone));
await page.screenshot({ path: OUT + '/phone-9digits-autosaved.png' });
// 2: Alt+Z undo
await page.keyboard.press('Alt+z');
await page.waitForTimeout(1500);
log('2 after Alt+Z shown', JSON.stringify(await page.locator(PHONE).inputValue()), 'stored', JSON.stringify((await stored()).phone));
// 3: national format with trunk 0
await open(); await page.locator(PHONE).click(); await page.keyboard.type('0505550199'); await page.waitForTimeout(1500);
log('3 typed 0505550199 -> stored', JSON.stringify((await stored()).phone));
// 4: other field dirty first, then phone complete -> must not save
await open();
const email = page.locator('[data-field="email"] input');
await email.click(); await email.press('End'); await page.keyboard.type('x');
await page.locator(PHONE).click(); await page.keyboard.type('521112233'); await page.waitForTimeout(1500);
log('4 email dirty then phone 521112233 -> stored', JSON.stringify((await stored()).phone), 'header', JSON.stringify(await page.locator('.record-header').textContent()));
await page.keyboard.press('Alt+z'); await page.waitForTimeout(500);
// 5: landline after 8 digits; mobile prefix 8 digits must not save
await open(); await page.locator(PHONE).click(); await page.keyboard.type('50555019'); await page.waitForTimeout(1200);
log('5 typed 8 digits of a mobile -> stored', JSON.stringify((await stored()).phone), 'shown', JSON.stringify(await page.locator(PHONE).inputValue()));
await page.keyboard.type('9'); await page.waitForTimeout(1200);
log('5b 9th digit -> stored', JSON.stringify((await stored()).phone));
// 6: foreign number
await open(); await page.locator(PHONE).click(); await page.keyboard.press('Control+a'); await page.keyboard.type('+44 20 7946 0958'); await page.waitForTimeout(800);
log('6 foreign typed -> shown', JSON.stringify(await page.locator(PHONE).inputValue()), 'stored', JSON.stringify((await stored()).phone));
await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
log('6b after Enter stored', JSON.stringify((await stored()).phone));
// 7: Enter right after autosave sends nothing more
await open(); await page.locator(PHONE).click(); await page.keyboard.type('+971505550199'); await page.waitForTimeout(1200);
const v1 = (await stored()).version; await page.keyboard.press('Enter'); await page.waitForTimeout(1000); const v2 = (await stored()).version;
log('7 compact typed -> stored', JSON.stringify((await stored()).phone), 'version before/after Enter', v1, v2);
// audit
const aud = await a.call('GET', `/api/audit/entries?take=10&search=${encodeURIComponent('ALN-DXB')}`);
log('audit status', aud.status, JSON.stringify(aud.body).slice(0, 600));
// restore
const now = await stored();
await a.call('PUT', '/api/tenancy/companies/' + co.id, { ...now, phone: rec0.phone });
log('restored', JSON.stringify((await stored()).phone));
await b.close();
