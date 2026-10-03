import { chromium } from 'playwright-core';
const B='http://localhost:20550'; const EV='/home/user/evidence-staging/p05-list-search/r1/';
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } }); const p = await ctx.newPage();
const errs=[]; p.on('console', m => { if (m.type()==='error') errs.push(m.text()); });
await p.goto(B + '/'); await p.locator('input[name="email"]:focus').waitFor();
await p.keyboard.type('admin.ar@alnoor.example'); await p.keyboard.press('Tab'); await p.keyboard.type('Demo-Pass-2026'); await p.keyboard.press('Enter');
await p.locator('nav.navpane a').first().waitFor();
console.log('dir', await p.evaluate(()=>[document.documentElement.dir, document.documentElement.lang]));
await p.locator('nav.navpane a[href="/identity/users"]').click(); await p.locator('main table tbody tr').first().waitFor(); await p.waitForTimeout(600);
const txt = (await p.locator('main').innerText()).split('\n').slice(0,40);
console.log(txt.join(' | '));
// Latin leftovers in chrome strings
const latin = await p.evaluate(()=>{const out=[]; for (const el of document.querySelectorAll('main th, main button, main .list-count, main input[placeholder], main h1, .list-hint, main label')){const s=(el.getAttribute('placeholder')||el.getAttribute('aria-label')||el.textContent||'').trim(); if(/[A-Za-z]{3,}/.test(s)) out.push(s.slice(0,50));} return out;});
console.log('latin strings in list chrome:', latin);
await p.screenshot({ path: EV+'08-users-list-ar-rtl.jpg', type:'jpeg', quality:70 });
await p.keyboard.type('Yousef Samir Wang'); await p.keyboard.press('Enter'); await p.waitForTimeout(800);
console.log('ar: Enter opened', p.url().includes('open='));
await p.keyboard.press('Escape'); await p.keyboard.press('Escape');
// header menu with mouse, in Arabic
await p.locator('thead button[aria-haspopup]').nth(1).click(); await p.waitForTimeout(300);
console.log('ar header menu:', await p.locator('[role=menu]').innerText().catch(()=>'-'));
const box = await p.locator('[role=menu]').boundingBox().catch(()=>null); console.log('menu box', box);
await p.screenshot({ path: EV+'09-header-menu-ar.jpg', type:'jpeg', quality:70 });
// Arabic search normalisation: create users with Arabic names via API is done elsewhere
console.log('errors', errs);
await browser.close();
