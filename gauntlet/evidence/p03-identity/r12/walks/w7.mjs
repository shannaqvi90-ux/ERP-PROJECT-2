// Walk 7: the r11 gap as a person meets it. Five anonymous wrong passwords from a script, then the
// account's owner on a new laptop (a fresh browser) types the right password.
import { open, signIn, EV, BASE, shot } from './h.mjs';
import fs from 'node:fs';
const log = []; const L = (s) => { log.push(s); console.log(s); };
const H = { 'content-type': 'application/json', 'X-Erp-Request': '1' };
const admin = (await (await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) })).json()).token;
const stamp = Date.now().toString(36);
const email = `finance.${stamp}@alnoor.example`;
const cr = await fetch(BASE + '/api/identity/users', { method: 'POST', headers: { ...H, authorization: 'Bearer ' + admin }, body: JSON.stringify({ email, displayName: 'Finance ' + stamp, displayNameAr: 'مالية', language: 'en', roleIds: [], password: 'Finance-Own-Pass-26', mustChangePassword: false }) });
L(`finance manager created: ${cr.status}`);
for (let i = 1; i <= 5; i++) { const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { ...H, 'user-agent': 'curl/8.5.0' }, body: JSON.stringify({ email, password: 'guess' + i }) }); L(`anonymous script attempt ${i}: ${r.status}`); }
const { browser, page } = await open({ width: 1366, height: 800 });
await signIn(page, email, 'Finance-Own-Pass-26');
L('owner, new laptop, right password: ' + (await page.locator('body').innerText()).slice(0, 400).replace(/\n/g, ' | '));
await shot(page, '12-owner-new-laptop-paused-by-anonymous-script-en');
fs.writeFileSync(`${EV}/walks/w7-owner-paused.txt`, log.join('\n'));
await browser.close();
