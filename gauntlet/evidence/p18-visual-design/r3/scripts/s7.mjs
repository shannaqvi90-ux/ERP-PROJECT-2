// s7: Arabic screens (home, contacts, record), the three themes, and whether theme and density follow the user
import { browser, signIn, shot, CUT, BASE } from './lib.mjs';
const b = await browser();
const ctx = await b.newContext({ viewport: { width: 1440, height: 900 } });
const page = await ctx.newPage();
const errors = []; page.on('pageerror', e => errors.push(String(e))); page.on('response', r => { if (r.status() >= 500) errors.push(r.status() + ' ' + r.url()); });
await signIn(page, 'admin.ar@alnoor.example');
console.log('ar home', JSON.stringify(await page.evaluate(() => ({ dir: document.documentElement.dir, lang: document.documentElement.lang, theme: document.documentElement.dataset.theme, text: document.querySelector('main').innerText.slice(0, 300).replace(/\n+/g, ' | ') }))));
await shot(page, '08-home-ar-dark');
await page.goto(BASE + '/contacts/contacts'); await page.waitForTimeout(3000);
const latin = await page.evaluate(() => { const t = document.querySelector('main').innerText; return (t.match(/\b[A-Za-z]{4,}\b/g) || []).filter(w => !/mail|example|Abdullah|Ahmed|Al|LLC|FZE|FZCO|Co|Trading|TRN/.test(w)).slice(0, 30); });
console.log('ar contacts latin words (data excluded roughly)', JSON.stringify(latin));
const cut = await page.evaluate(CUT); console.log('ar contacts cut', cut.length, 'untitled', cut.filter(c => !c.titled).length, JSON.stringify(cut.filter(c => !c.titled).slice(0, 6)));
// Open a record and look at the dates and amounts
await page.locator('tbody tr').nth(3).click(); await page.keyboard.press('Enter'); await page.waitForTimeout(2000);
const vals = await page.evaluate(() => [...document.querySelectorAll('main input')].filter(i => i.value && /\d/.test(i.value)).map(i => i.name + '=' + i.value).slice(0, 20));
console.log('ar record values', JSON.stringify(vals));
await shot(page, '09-contact-open-ar-dark');
// Themes: click light, then high contrast (buttons in the top bar)
const themeButtons = await page.evaluate(() => [...document.querySelectorAll('header button')].map(b => b.getAttribute('aria-label') || b.title).filter(Boolean));
console.log('header buttons', JSON.stringify(themeButtons));
async function setTheme(i) { const btns = page.locator('header [aria-pressed]'); console.log('pressed-buttons', await btns.count()); await btns.nth(i).click(); await page.waitForTimeout(800); return page.evaluate(() => document.documentElement.dataset.theme); }
console.log('theme ->', await setTheme(2)); await shot(page, '10-contact-open-ar-high-contrast');
console.log('theme ->', await setTheme(0));
await page.getByText('مريح', { exact: true }).click().catch(e => console.log('no comfortable label', String(e).slice(0, 80)));
await page.waitForTimeout(500);
console.log('after set', JSON.stringify(await page.evaluate(() => ({ theme: document.documentElement.dataset.theme, density: document.documentElement.dataset.density, ls: Object.keys(localStorage) }))));
// Sign out and sign in as another person on the same browser
await page.goto(BASE + '/');
const out = page.locator('header button').last(); await out.click(); await page.waitForTimeout(2000);
await signIn(page, 'viewer@alnoor.example');
console.log('next user on same device', JSON.stringify(await page.evaluate(() => ({ theme: document.documentElement.dataset.theme, density: document.documentElement.dataset.density }))));
// Same admin.ar on a fresh device: is the choice remembered for the user?
const ctx2 = await b.newContext({ viewport: { width: 1440, height: 900 } }); const p2 = await ctx2.newPage();
await signIn(p2, 'admin.ar@alnoor.example');
console.log('admin.ar on another device', JSON.stringify(await p2.evaluate(() => ({ theme: document.documentElement.dataset.theme, density: document.documentElement.dataset.density }))));
console.log('errors', JSON.stringify(errors));
await b.close();
