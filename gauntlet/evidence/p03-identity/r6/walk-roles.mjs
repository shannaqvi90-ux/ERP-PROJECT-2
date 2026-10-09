import { open, signIn, BASE, shot } from './pw.mjs';
const { browser, page, errors } = await open(1600, 900);
await signIn(page, 'admin@alnoor.example');
await page.goto(BASE + '/identity/roles'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(800);
await page.keyboard.press('Alt+n'); await page.waitForTimeout(1200);
console.log('focused after Alt+N:', await page.evaluate(() => document.activeElement?.outerHTML.slice(0, 200)));
const body1 = await page.locator('body').innerText();
console.log('NEW ROLE PANEL:\n' + body1.slice(body1.indexOf('New role'), body1.indexOf('New role') + 2500));
await shot(page, '02-new-role-matrix-en');
// permission search 'view' then Select all shown
const search = page.getByPlaceholder(/search permissions|find a permission|filter permissions/i).first();
if (await search.count()) { await search.fill('view'); } else { console.log('NO PERMISSION SEARCH FOUND'); }
await page.waitForTimeout(600);
const selAll = page.getByRole('button', { name: /select all shown|all shown|tick all/i }).first();
console.log('select-all button count', await selAll.count(), await selAll.count() ? await selAll.innerText() : '');
if (await selAll.count()) await selAll.click();
await page.waitForTimeout(600);
await shot(page, '03-role-matrix-view-select-all-en');
const checked = await page.locator('input[type=checkbox]:checked').evaluateAll(els => els.map(e => e.getAttribute('aria-label') || e.name || e.value || e.id));
console.log('checked after view+select all:', checked.length, JSON.stringify(checked));
console.log('errors', errors);
await browser.close();
