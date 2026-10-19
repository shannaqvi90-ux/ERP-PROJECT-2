// Critic p03 r13 walk 2: the shared-address pause in the browser. An anonymous script fails 5 times on the account;
// the owner on a new laptop is paused and shown a device code; the administrator lets that device in from the user's
// sign-in history (by keyboard); the laptop signs in. English and Arabic.
import { launch, BASE, signIn, shot, logger } from './lib.mjs';
const lang = process.env.LANG_UI || 'en';
const L = logger('w2-device-approval-' + lang);
const email = process.env.EMAIL || (lang === 'en' ? 'accountant@alnoor.example' : 'viewer@alnoor.example');
const H = { 'content-type': 'application/json', 'X-Erp-Request': '1', 'user-agent': 'curl/8.5.0' };
for (let i = 1; i <= 5; i++) { const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email, password: 'guess-' + i }) }); L(`script attempt ${i}: ${r.status}`); }
const b = await launch();
const errs=[];
const laptop = await b.newContext({ viewport: { width: 1366, height: 800 }, locale: lang === 'ar' ? 'ar-AE' : 'en-US' }); const q = await laptop.newPage();
q.on('console', m => { if (m.type()==='error' && !/429/.test(m.text())) errs.push(m.text()); }); q.on('response', r => { if (r.status() >= 500) errs.push('5xx ' + r.url()); });
await q.goto(BASE + '/'); await q.waitForTimeout(800);
if (lang === 'ar') { await q.getByRole('button', { name: 'العربية' }).click().catch(()=>{}); await q.waitForTimeout(600); }
await signIn(q, email); await q.waitForTimeout(1500);
const msg = await q.locator('body').innerText();
L('laptop sign-in screen: ' + msg.replace(/\n/g, ' | ').slice(0, 900));
L('dir=' + await q.evaluate(() => document.documentElement.dir));
const code = msg.match(/[A-Z0-9]{4}-[A-Z0-9]{4}/)?.[0];
L('device code shown: ' + code);
await shot(q, `0${lang === 'en' ? 3 : 4}-new-laptop-paused-device-code-${lang}.jpg`);
// the administrator
const adm = await b.newContext({ viewport: { width: 1366, height: 800 } }); const p = await adm.newPage();
p.on('console', m => { if (m.type()==='error') errs.push('admin: ' + m.text()); });
await signIn(p, lang === 'en' ? 'admin@alnoor.example' : 'admin.ar@alnoor.example'); await p.waitForURL(BASE + '/'); await p.waitForTimeout(800);
await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForURL(/identity\/users/); await p.waitForTimeout(800);
await p.keyboard.type(email); await p.waitForTimeout(1200); await p.keyboard.press('Enter'); await p.waitForTimeout(1200);
await p.keyboard.press('Control+k'); await p.waitForTimeout(400); await p.keyboard.type(lang === 'en' ? 'history' : 'سجل'); await p.waitForTimeout(700);
L('palette for history offers: ' + (await p.locator('[role=option]').allInnerTexts()).join(' / ').slice(0, 300));
if (await p.locator('[role=option]').count()) { await p.keyboard.press('Enter'); } else { await p.keyboard.press('Escape'); await p.getByRole('tab', { name: lang === 'en' ? 'Sign-in history' : /سجل/ }).click(); }
await p.waitForTimeout(1200);
// the history tab: find by palette command
await p.keyboard.press('Control+k'); await p.waitForTimeout(400); await p.keyboard.type(lang === 'en' ? 'device' : 'جهاز'); await p.waitForTimeout(700);
L('palette offers: ' + (await p.locator('[role=option]').allInnerTexts()).join(' / ').slice(0, 400));
await p.keyboard.press('Enter'); await p.waitForTimeout(1000);
const focused = await p.evaluate(() => { const e = document.activeElement; return (e?.labels?.[0]?.innerText || e?.getAttribute('aria-label') || e?.tagName) + ' ' + (e?.getAttribute('inputmode')||''); });
L('focused after the palette command: ' + focused);
const panel = await p.locator('main').innerText();
L('history tab: ' + panel.replace(/\n/g, ' | ').slice(panel.indexOf(lang==='en'?'Sign-in':'تسجيل'), panel.indexOf(lang==='en'?'Sign-in':'تسجيل') + 900));
await p.keyboard.type((code || '').toLowerCase()); await p.keyboard.press('Enter'); await p.waitForTimeout(1200);
L('after approving: ' + (await p.locator('main').innerText()).replace(/\n/g, ' | ').match(/.{0,200}(device|جهاز).{0,200}/i)?.[0]);
await shot(p, `0${lang === 'en' ? 5 : 6}-admin-lets-device-in-history-${lang}.jpg`);
// the laptop again (the same browser), right password
await q.getByLabel(/password|كلمة المرور/i).first().fill('Demo-Pass-2026'); await q.keyboard.press('Enter'); await q.waitForTimeout(2000);
L('laptop after approval: ' + q.url() + ' ' + (await q.locator('body').innerText()).replace(/\n/g, ' | ').slice(0, 200));
// another fresh browser, still paused?
const other = await b.newContext(); const o = await other.newPage(); await signIn(o, email); await o.waitForTimeout(1500);
L('another fresh browser: ' + (await o.locator('body').innerText()).replace(/\n/g, ' | ').match(/.{0,80}(paused|متوقف|موقوف|معلق).{0,80}/)?.[0]);
L('console errors / 5xx: ' + JSON.stringify(errs));
L.save(); await b.close();
