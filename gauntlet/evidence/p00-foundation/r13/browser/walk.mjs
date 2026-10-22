// Critic p00 r13 browser walk (own script, built on the r12 critic's): sign-in EN/AR, keyboard
// sign-in and sign-out, shells, Tab traversal, restricted users, Arabic layout checks.
import { chromium } from '/home/shan/critic/p00-foundation-r13/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20050';
const OUT = '/home/shan/evidence-staging/p00-foundation/r13/';
const exe = process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
const browser = await chromium.launch({ executablePath: exe });
const log = (...a) => console.log(...a);
async function ctx(locale = 'en-US') { const c = await browser.newContext({ locale, viewport: { width: 1366, height: 800 } }); return [c, await c.newPage()]; }
const shot = (p, n) => p.screenshot({ path: OUT + n, type: 'jpeg', quality: 65 });
const info = async (p) => p.evaluate(() => ({ lang: document.documentElement.lang, dir: document.documentElement.dir, title: document.title, text: document.body.innerText.slice(0, 700) }));
const active = (p) => p.evaluate(() => { const e = document.activeElement; return `${e.tagName}:${e.getAttribute('aria-label') || e.getAttribute('name') || e.textContent?.trim().slice(0, 30)}`; });
async function kbSignIn(p, email) {
  await p.goto(BASE); await p.waitForLoadState('networkidle');
  const focus = await active(p);
  const t0 = Date.now();
  await p.keyboard.type(email); await p.keyboard.press('Enter');
  await p.waitForTimeout(300);
  await p.keyboard.type('Demo-Pass-2026'); await p.keyboard.press('Enter');
  await p.getByText(/Sign out|تسجيل الخروج/).first().waitFor({ timeout: 20000 });
  await p.waitForLoadState('networkidle');
  return { focus, ms: Date.now() - t0 };
}
let [c, p] = await ctx('en-US');
await p.goto(BASE); await p.waitForLoadState('networkidle');
log('EN sign-in', JSON.stringify(await info(p)));
await shot(p, '01-sign-in-en.jpg');
const tabs = []; for (let i = 0; i < 8; i++) { await p.keyboard.press('Tab'); tabs.push(await active(p)); }
log('EN sign-in tab order', tabs.join(' | '));
await c.close();

[c, p] = await ctx('ar-AE');
await p.goto(BASE); await p.waitForLoadState('networkidle');
const arSign = await info(p);
log('AR sign-in', JSON.stringify(arSign));
log('AR sign-in Latin tokens', (arSign.text.match(/[A-Za-z][A-Za-z0-9.@-]*/g) || []).join(' '));
await shot(p, '02-sign-in-ar.jpg');
await c.close();

[c, p] = await ctx('en-US');
let r = await kbSignIn(p, 'admin@alnoor.example');
log('EN admin keyboard sign-in', JSON.stringify(r), JSON.stringify(await info(p)), p.url());
await shot(p, '03-shell-en.jpg');
const seen = new Set(); let body = 0;
for (let i = 0; i < 40; i++) { await p.keyboard.press('Tab'); const a = await active(p); if (a.startsWith('BODY')) body++; seen.add(a); }
log('EN shell 40 tabs: body stops', body, 'distinct', seen.size, [...seen].slice(0, 25).join(' | '));
// Command palette by keyboard
await p.keyboard.press('Control+k'); await p.waitForTimeout(300); await p.keyboard.type('Roles'); await p.keyboard.press('Enter'); await p.waitForTimeout(1200);
log('Ctrl+K Roles ->', p.url());
// Sign out by keyboard: find the control and activate with Enter
await p.getByRole('button', { name: /Sign out/ }).first().focus(); await p.keyboard.press('Enter');
await p.waitForTimeout(1500);
log('after sign out', p.url(), (await info(p)).text.slice(0, 120).replace(/\n/g, ' / '));
const after = await p.evaluate(() => fetch('/api/identity/users?take=1', { headers: { 'X-Erp-Request': '1' } }).then(r => r.status));
log('API after sign out', after);
await c.close();

[c, p] = await ctx('en-US');
r = await kbSignIn(p, 'admin.ar@alnoor.example');
const ar = await info(p);
log('AR admin shell', JSON.stringify(r), JSON.stringify(ar), p.url());
log('AR shell Latin tokens', (ar.text.match(/[A-Za-z][A-Za-z0-9.@-]*/g) || []).join(' '));
await shot(p, '04-shell-ar.jpg');
await p.keyboard.press('Control+k'); await p.waitForTimeout(300); await p.keyboard.type('الأدوار'); await p.keyboard.press('Enter'); await p.waitForTimeout(1500);
log('AR Ctrl+K الأدوار ->', p.url(), (await info(p)).dir);
await shot(p, '05-roles-ar.jpg');
await c.close();

[c, p] = await ctx('en-US');
r = await kbSignIn(p, 'noaccess@alnoor.example');
log('no-roles shell', JSON.stringify(await info(p)));
log('no-roles API users', await p.evaluate(() => fetch('/api/identity/users?take=1', { headers: { 'X-Erp-Request': '1' } }).then(r => r.status)));
await shot(p, '06-no-roles-user.jpg');
await c.close();

[c, p] = await ctx('en-US');
r = await kbSignIn(p, 'viewer@alnoor.example');
await p.goto(BASE + '/identity/roles').catch(() => {}); await p.waitForTimeout(1500);
const vtext = (await info(p)).text;
log('viewer roles screen', p.url(), 'has New button:', await p.getByRole('button', { name: /^New/ }).count(), vtext.slice(0, 200).replace(/\n/g, ' / '));
log('viewer POST role', await p.evaluate(() => fetch('/api/identity/roles', { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ nameEn: 'x', nameAr: 'x', permissions: [] }) }).then(r => r.status)));
await c.close();
await browser.close();
