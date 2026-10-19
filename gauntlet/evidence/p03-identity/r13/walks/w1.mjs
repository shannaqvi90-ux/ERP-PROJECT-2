// Critic p03 r13 walk 1: create a user with a restricted (read-only) role by keyboard; sign in as them with the set-up code; what they can see and do (screen and API).
import { launch, BASE, signIn, shot, logger } from './lib.mjs';
const L = logger('w1-restricted-user');
const b = await launch(); const ctx = await b.newContext({ viewport: { width: 1366, height: 800 } }); const p = await ctx.newPage();
const errs=[]; p.on('console', m => { if (m.type()==='error') errs.push(m.text()); }); p.on('response', r => { if (r.status() >= 500) errs.push('5xx ' + r.url()); });
await signIn(p, 'admin@alnoor.example'); await p.waitForURL(BASE + '/'); await p.waitForTimeout(800);
await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForURL(/identity\/users/); await p.waitForTimeout(800);
await p.keyboard.press('Alt+n'); await p.waitForTimeout(800);
const stamp = Date.now().toString(36);
const email = `clerk.${stamp}@alnoor.example`;
L('focused after Alt+N: ' + await p.evaluate(() => document.activeElement?.getAttribute('aria-label') || document.activeElement?.labels?.[0]?.innerText || document.activeElement?.name));
await p.keyboard.type(email);
L('panel text: ' + (await p.locator('main').innerText()).slice(0, 900).replace(/\n/g, ' | '));
await p.keyboard.press('Alt+o'); await p.waitForTimeout(400); await p.keyboard.type('read'); await p.waitForTimeout(500); await p.keyboard.press('Enter'); await p.waitForTimeout(400);
await p.keyboard.press('Control+Enter'); await p.waitForTimeout(1500);
const text = await p.locator('main').innerText();
const code = text.match(/[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}(-[A-Z0-9]{4})?/)?.[0];
L('after save: ' + text.slice(0, 700).replace(/\n/g, ' | '));
L('set-up code: ' + code);
await shot(p, '01-new-read-only-user-by-keyboard-set-up-code-en.jpg');
// the new user signs in with the code
const c2 = await b.newContext({ viewport: { width: 1366, height: 800 } }); const q = await c2.newPage();
await signIn(q, email, code); await q.waitForTimeout(1500);
L('new user first sign-in screen: ' + (await q.locator('body').innerText()).slice(0, 300).replace(/\n/g, ' | '));
const pw = 'Clerk-Pass-2026!x';
const pwFields = q.locator('input[type=password]');
L('password fields: ' + await pwFields.count());
await q.getByLabel('New password', { exact: true }).fill(pw); await q.getByLabel('Repeat the new password').fill(pw);
await q.keyboard.press('Enter'); await q.waitForTimeout(2000);
L('after choosing password: ' + q.url() + ' ' + (await q.locator('body').innerText()).slice(0, 400).replace(/\n/g, ' | '));
await q.goto(BASE + '/identity/users'); await q.waitForTimeout(2000);
const ut = await q.locator('main').innerText();
L('restricted user, users list: new-user button? ' + (await q.getByRole('button', { name: /new user/i }).count()) + ' | ' + ut.slice(0, 200).replace(/\n/g, ' | '));
await q.keyboard.press('Alt+n'); await q.waitForTimeout(600);
L('Alt+N as read-only opens a form? ' + (await q.locator('input[type=email]').count()));
// open one user's record
await q.locator('[role=row]').nth(2).click().catch(() => {}); await q.waitForTimeout(1200);
L('record as read-only: editable inputs ' + await q.locator('main input:not([disabled]):not([readonly]):not([type=search])').count() + ' save buttons ' + await q.getByRole('button', { name: /^save/i }).count());
await shot(q, '02-read-only-user-record-no-actions-en.jpg');
await q.goto(BASE + '/identity/roles'); await q.waitForTimeout(1500);
L('roles screen as read-only: ' + (await q.locator('main').innerText()).slice(0, 200).replace(/\n/g, ' | '));
// API as the restricted user
const api = async (m, path, body) => q.evaluate(async ([m, path, body]) => { const r = await fetch(path, { method: m, headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: body ? JSON.stringify(body) : undefined }); return r.status + ' ' + (await r.text()).slice(0, 90); }, [m, path, body]);
L('API GET users: ' + await api('GET', '/api/identity/users?take=1'));
L('API POST users: ' + await api('POST', '/api/identity/users', { email: `x.${stamp}@alnoor.example`, displayName: 'X', language: 'en', roleIds: [] }));
L('API POST roles: ' + await api('POST', '/api/identity/roles', { nameEn: 'R ' + stamp, nameAr: 'ر ' + stamp, permissions: [] }));
L('API GET companies of tenancy: ' + await api('GET', '/api/tenancy/companies'));
L('console errors / 5xx: ' + JSON.stringify(errs));
L.save(); await b.close();
