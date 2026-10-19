// Critic p00 r12: no-roles user, read-only user, empty workspace first sign-in (temporary password), lockout.
import { chromium } from '/home/shan/critic/p00-foundation-r12/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20050';
const OUT = '/home/shan/evidence-staging/p00-foundation/r12/';
const exe = process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
const browser = await chromium.launch({ executablePath: exe });
const log = (...a) => console.log(...a);
async function ctx(locale = 'en-US') { const c = await browser.newContext({ locale, viewport: { width: 1366, height: 800 } }); return [c, await c.newPage()]; }
const shot = (p, n) => p.screenshot({ path: OUT + n, type: 'jpeg', quality: 70 });
const text = (p) => p.evaluate(() => document.body.innerText.slice(0, 900));
async function signIn(p, email, pw, wait = true) {
  await p.goto(BASE); await p.waitForLoadState('networkidle');
  await p.keyboard.type(email); await p.keyboard.press('Enter'); await p.waitForTimeout(200);
  await p.keyboard.type(pw); await p.keyboard.press('Enter');
  if (wait) await p.getByText(/Sign out|تسجيل الخروج/).first().waitFor({ timeout: 15000 });
  else await p.waitForTimeout(2000);
}
const which = process.argv[2] ?? 'all';
if (which === 'all' || which === 'roles') {
  let [c, p] = await ctx();
  await signIn(p, 'noaccess@alnoor.example', 'Demo-Pass-2026');
  log('NOACCESS shell:', JSON.stringify(await text(p)));
  await shot(p, '05-no-roles-user.jpg');
  for (const u of ['/api/identity/users?take=1', '/api/identity/roles?take=1', '/api/tenancy/companies?take=1', '/api/reports/identity.users?format=csv']) {
    const r = await p.evaluate(async (u) => (await fetch(u, { headers: { 'X-Erp-Request': '1' } })).status, u);
    log('NOACCESS GET', u, r);
  }
  await p.goto(BASE + '/identity/users'); await p.waitForTimeout(1500);
  log('NOACCESS direct /identity/users:', JSON.stringify((await text(p)).slice(0, 300)));
  await c.close();
  [c, p] = await ctx();
  await signIn(p, 'viewer@alnoor.example', 'Demo-Pass-2026');
  await p.goto(BASE + '/identity/users'); await p.waitForLoadState('networkidle'); await p.waitForTimeout(1500);
  const btns = await p.evaluate(() => [...document.querySelectorAll('button')].map(b => b.innerText.trim()).filter(Boolean));
  log('VIEWER users buttons:', btns.join(' | '));
  await shot(p, '06-read-only-users-list.jpg');
  const r = await p.evaluate(async () => (await fetch('/api/identity/roles', { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ nameEn: 'x', nameAr: 'x', permissions: [] }) })).status);
  log('VIEWER POST role', r);
  await c.close();
}
if (which === 'all' || which === 'empty') {
  const [c, p] = await ctx('ar-AE');
  await signIn(p, 'owner@emptyco.example', 'Initial-Pass-2026x', false);
  log('EMPTYCO after temp password:', JSON.stringify(await text(p)), await p.evaluate(() => document.documentElement.dir));
  await shot(p, '07-first-sign-in-own-password.jpg');
  const focus = await p.evaluate(() => document.activeElement?.closest('label')?.innerText || document.activeElement?.tagName);
  log('EMPTYCO focus', focus);
  // Choose own password by keyboard.
  await p.keyboard.type('Owner-Own-Pass-2026'); await p.keyboard.press('Tab'); await p.keyboard.type('Owner-Own-Pass-2026'); await p.keyboard.press('Enter');
  await p.getByText(/Sign out|تسجيل الخروج/).first().waitFor({ timeout: 15000 }).catch(e => log('no shell', e.message));
  log('EMPTYCO shell:', JSON.stringify(await text(p)), await p.evaluate(() => document.documentElement.dir));
  await shot(p, '08-empty-workspace-home.jpg');
  // Old password must not work any more; the new one must.
  const st = async (pw) => (await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ email: 'owner@emptyco.example', password: pw }) })).status;
  log('EMPTYCO temp password again ->', await st('Initial-Pass-2026x'), ' own password ->', await st('Owner-Own-Pass-2026'));
  await c.close();
}
if (which === 'lockout') {
  const [c, p] = await ctx();
  for (let i = 0; i < 6; i++) { await signIn(p, 'viewer@alnoor.example', 'wrong-pass-' + i, false); }
  log('LOCKOUT after 6 wrong:', JSON.stringify(await text(p)));
  await signIn(p, 'viewer@alnoor.example', 'Demo-Pass-2026', false);
  log('LOCKOUT right password:', JSON.stringify(await text(p)));
  await shot(p, '09-lockout-message.jpg');
  await c.close();
}
await browser.close();
