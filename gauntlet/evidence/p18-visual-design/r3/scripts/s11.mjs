// s11: reviewers' named faults (credit-limit currency, attachments Delete buttons), empty state of a list, exchange rates in high contrast
import { browser, signIn, shot, CUT, BASE } from './lib.mjs';
const lang = process.env.LANG2 || 'en';
const b = await browser();
const page = await (await b.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
await signIn(page, lang === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example');
await page.goto(BASE + '/contacts/contacts'); await page.waitForTimeout(2500);
await page.locator('tbody tr').nth(0).click(); await page.keyboard.press('Enter'); await page.waitForTimeout(2000);
await page.locator('.section-tabs button').nth(4).click(); await page.waitForTimeout(600);
console.log('money fields', JSON.stringify(await page.evaluate(() => [...document.querySelectorAll('main input')].filter(i => /credit|limit|amount/i.test(i.name)).map(i => { const box = i.closest('label, .field'); return { name: i.name, value: i.value, around: box?.innerText.replace(/\n+/g, ' | ').slice(0, 120), cut: i.scrollWidth > i.clientWidth + 1 }; }))));
// Attachments panel
await page.keyboard.press('Alt+a'); await page.waitForTimeout(1500);
console.log('attachment buttons', JSON.stringify(await page.evaluate(() => [...document.querySelectorAll('[data-testid=attachments] button')].map(b => ({ t: b.textContent.trim() || b.getAttribute('aria-label'), cut: b.scrollWidth > b.clientWidth + 1 })).slice(0, 12))));
// Empty state
await page.goto(BASE + '/contacts/contacts'); await page.waitForTimeout(2000);
await page.keyboard.press('/'); await page.keyboard.type('zzqqxxnomatch'); await page.waitForTimeout(2000);
console.log('empty state', JSON.stringify(await page.evaluate(() => document.querySelector('main')?.innerText.split('\n').filter(l => /no |match|نتائج|لا /i.test(l)).slice(0, 4))));
// Exchange rates in high contrast: does the toolbar wrap, does the key hint sit under the status bar?
await page.locator('header [aria-pressed]').nth(2).click(); await page.waitForTimeout(400);
await page.goto(BASE + '/currency/rates'); await page.waitForTimeout(2500);
console.log('rates hc', JSON.stringify(await page.evaluate(() => { const tb = document.querySelector('.list-toolbar, .page-header, main header'); const hint = [...document.querySelectorAll('main *')].find(e => /move/.test(e.textContent) && e.children.length < 3 && /Enter/.test(e.textContent)); const sb = document.querySelector('footer, .status-bar'); return { toolbarH: tb?.getBoundingClientRect().height, hintBottom: hint?.getBoundingClientRect().bottom, statusTop: sb?.getBoundingClientRect().top }; })));
await page.locator('header [aria-pressed]').nth(1).click(); await page.waitForTimeout(400);
await b.close();
