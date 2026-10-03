import { chromium } from 'playwright';
const BASE = 'http://localhost:20050';
const OUT = '/home/user/evidence-staging/p00-foundation/r2/';
const PW = 'Demo-Pass-2026';
const log = [];
const note = (s) => { log.push(s); console.log(s); };
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const shot = async (page, name) => page.screenshot({ path: OUT + name, type: 'jpeg', quality: 70 });
const consoleErrors = [];
async function newPage(locale = 'en-US') {
  const ctx = await browser.newContext({ locale, viewport: { width: 1280, height: 800 } });
  const page = await ctx.newPage();
  page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
  page.on('pageerror', e => consoleErrors.push(String(e)));
  return page;
}
// 1. English first screen, keyboard sign-in
let page = await newPage('en-US');
let t0 = Date.now();
await page.goto(BASE + '/');
await page.locator('input[name="email"]').waitFor();
note(`first screen ready ${Date.now() - t0} ms; html lang=${await page.getAttribute('html','lang')} dir=${await page.getAttribute('html','dir')}; focused=${await page.evaluate(() => document.activeElement?.getAttribute('name'))}`);
await shot(page, '01-signin-en.jpg');
let keys = 0;
const type = async (s) => { await page.keyboard.type(s); keys += s.length; };
const press = async (k) => { await page.keyboard.press(k); keys += 1; };
t0 = Date.now();
await type('admin@alnoor.example'); await press('Tab'); await type(PW); await press('Enter');
await page.getByRole('heading', { name: /Welcome/ }).waitFor();
note(`EN keyboard sign-in: steps=4 (type e-mail, Tab, type password, Enter) keystrokes=${keys} machine=${Date.now() - t0} ms (includes typing at playwright speed)`);
await shot(page, '02-shell-en.jpg');
// keyboard navigation of the shell: Tab order
const order = [];
for (let i = 0; i < 9; i++) { await page.keyboard.press('Tab'); order.push(await page.evaluate(() => { const e = document.activeElement; return `${e.tagName}:${(e.textContent||'').trim().slice(0,25)}`; })); }
note('Tab order after sign-in: ' + order.join(' | '));
const fv = await page.evaluate(() => getComputedStyle(document.activeElement).outlineStyle + ' ' + getComputedStyle(document.activeElement).outlineWidth);
note('focus outline on focused element: ' + fv);
// 2. Users: find one among 100k
await page.goto(BASE + '/identity/users');
await page.locator('table tbody tr').first().waitFor();
const total = await page.locator('.muted').first().textContent();
note('users count label: ' + total);
const target = 'ahmed.alketbi.12';
t0 = Date.now();
const search = page.getByRole('searchbox');
await search.focus();
await page.keyboard.type('fatima.smith.51179');
await page.waitForFunction(() => document.querySelectorAll('table tbody tr').length <= 3);
note(`find one among 100k by typing a unique e-mail fragment: ${Date.now() - t0} ms (includes 200 ms debounce); rows=${await page.locator('table tbody tr').count()}; label=${await page.locator('.muted').first().textContent()}`);
await shot(page, '03-users-find-en.jpg');
// measure raw API search over 100k (name substring)
for (const q of ['fatima', 'zaabi.734', 'Omar Haddad', 'zzzz-none']) {
  const r = await page.evaluate(async (q) => { const t = performance.now(); const res = await fetch('/api/identity/users?take=50&search=' + encodeURIComponent(q)); const j = await res.json(); return { ms: Math.round(performance.now() - t), total: j.total }; }, q);
  note(`API search "${q}": ${r.ms} ms, total ${r.total}`);
}
// page 2 deep paging
const deep = await page.evaluate(async () => { const t = performance.now(); const res = await fetch('/api/identity/users?take=50&skip=99900'); const j = await res.json(); return { ms: Math.round(performance.now() - t), n: j.items.length }; });
note(`deep page skip=99900: ${deep.ms} ms, ${deep.n} rows`);
// 3. Roles & workspace
await page.goto(BASE + '/identity/roles');
await page.waitForTimeout(800);
await shot(page, '04-roles-en.jpg');
await page.goto(BASE + '/tenancy/tenant').catch(() => {});
await page.waitForTimeout(800);
note('workspace page url: ' + page.url() + ' h1=' + await page.locator('h1').first().textContent().catch(() => ''));
// 4. Switch to Arabic in the shell by keyboard: find the toggle
const toggle = page.getByRole('button', { name: 'العربية' });
await toggle.focus();
const putDone = page.waitForResponse(r => r.url().includes('/api/identity/me/preferences'), { timeout: 5000 }).then(r => r.status()).catch(() => 'no request');
const tToggle = Date.now();
await page.keyboard.press('Enter');
await page.waitForFunction(() => document.documentElement.dir === 'rtl');
note(`switch to Arabic in the shell: 1 step (focus toggle + Enter), ${Date.now() - tToggle} ms to RTL; profile save: ${await putDone}`);
note(`after toggle: lang=${await page.getAttribute('html','lang')} dir=${await page.getAttribute('html','dir')}`);
await page.goto(BASE + '/identity/users');
await page.locator('table tbody tr').first().waitFor();
await page.waitForTimeout(500);
note('AR users label: ' + await page.locator('.muted').first().textContent());
await shot(page, '05-users-ar.jpg');
await page.goto(BASE + '/identity/roles'); await page.waitForTimeout(600);
await shot(page, '06-roles-ar.jpg');
// untranslated English left on Arabic screens?
const latin = await page.evaluate(() => { const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT); const out = []; while (w.nextNode()) { const s = w.currentNode.textContent.trim(); if (s && /[A-Za-z]{3,}/.test(s) && !/@/.test(s)) out.push(s.slice(0, 50)); } return out.slice(0, 30); });
note('Latin text on Arabic roles screen: ' + JSON.stringify(latin));
// sign out
await page.locator('.topbar-end button.button.ghost').last().click();
await page.locator('input[name="password"]').waitFor();
await shot(page, '07-signin-ar-after-signout.jpg');
note(`after sign-out: dir=${await page.getAttribute('html','dir')} focused=${await page.evaluate(() => document.activeElement?.getAttribute('name'))}`);
await page.goBack(); await page.waitForTimeout(800);
note('back after sign-out shows: ' + (await page.locator('input[name="password"]').count() ? 'sign-in screen' : page.url()));
// 5. Arabic browser, first screen
page = await newPage('ar-AE');
await page.goto(BASE + '/');
await page.locator('input[name="email"]').waitFor();
note(`ar-AE browser first screen: lang=${await page.getAttribute('html','lang')} dir=${await page.getAttribute('html','dir')}`);
keys = 0;
t0 = Date.now();
await type('admin.ar@alnoor.example'); await press('Tab'); await type('wrong-password'); await press('Enter');
await page.getByRole('alert').waitFor();
note('AR wrong password message: ' + await page.getByRole('alert').textContent());
await shot(page, '08-signin-ar-error.jpg');
await type(PW); await press('Enter');
await page.getByRole('heading').first().waitFor();
await page.waitForTimeout(400);
note('AR home heading: ' + await page.getByRole('heading').first().textContent());
await shot(page, '09-shell-ar.jpg');
// 6. noaccess user
page = await newPage('en-US');
await page.goto(BASE + '/');
await page.locator('input[name="email"]').fill('noaccess@alnoor.example');
await page.locator('input[name="password"]').fill(PW);
await page.keyboard.press('Enter');
await page.waitForTimeout(1000);
await page.goto(BASE + '/identity/roles'); await page.waitForTimeout(800);
note('noaccess at /identity/roles: ' + await page.locator('h1').first().textContent());
await shot(page, '10-noaccess-direct-url.jpg');
// 7. viewer: can it see create/edit controls?
page = await newPage('en-US');
await page.goto(BASE + '/');
await page.locator('input[name="email"]').fill('viewer@alnoor.example');
await page.locator('input[name="password"]').fill(PW);
await page.keyboard.press('Enter');
await page.waitForTimeout(1000);
await page.goto(BASE + '/identity/users'); await page.waitForTimeout(800);
note('viewer users screen buttons: ' + JSON.stringify(await page.locator('main button').allTextContents()));
note('console errors: ' + JSON.stringify(consoleErrors));
await browser.close();
import fs from 'fs'; fs.writeFileSync(OUT + 'browser-walk.txt', log.join('\n') + '\n');
