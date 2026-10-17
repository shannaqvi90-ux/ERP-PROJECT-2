// An Administrator who works in no company opens Companies. node no-company-admin.mjs <base> <shot-name>
import { createRequire } from 'node:module';
const require = createRequire('/home/shan/critic/p02-tenancy-r10/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
const [base, shotName] = process.argv.slice(2);
const pw = 'Critic-R10-Nc-' + Math.random().toString(36).slice(2, 8) + '!A1';
const api = async (token, m, p, b) => { const r = await fetch(base + p, { method: m, headers: { authorization: 'Bearer ' + token, 'x-erp-request': '1', 'content-type': 'application/json' }, body: b ? JSON.stringify(b) : undefined }); const t = await r.text(); try { return [r.status, JSON.parse(t)]; } catch { return [r.status, t]; } };
const admin = (await (await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) })).json()).token;
const roles = (await api(admin, 'GET', '/api/identity/roles?take=50'))[1].items;
const adminRole = roles.find((r) => /^administrator/i.test(r.name ?? r.nameEn ?? ''));
const email = `critic.r10.nocompany.${Date.now().toString(36)}@alnoor.example`;
const [cs, user] = await api(admin, 'POST', '/api/identity/users', { email, displayName: 'Critic R10 no company', language: 'en', password: pw, mustChangePassword: false, roleIds: [adminRole.id] });
const acc = (await api(admin, 'GET', `/api/tenancy/access/${user.id}`))[1];
console.log('created', cs, 'access before', JSON.stringify((acc.companies ?? []).filter((c) => c.granted !== false).map((c) => c.companyId ?? c)).slice(0, 120));
const [ps] = await api(admin, 'PUT', `/api/tenancy/access/${user.id}`, { companies: [], version: acc.version });
console.log('access set to no company:', ps);
const browser = await chromium.launch({ executablePath: `${process.env.HOME}/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome` });
const page = await (await browser.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
await page.goto(base + '/'); await page.locator('input[name="email"]:focus').waitFor();
await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type(pw); await page.keyboard.press('Enter');
await page.locator('main h1').waitFor();
console.log('menu has Companies:', await page.locator('nav a[href="/tenancy/companies"]').count() > 0);
await page.goto(base + '/tenancy/companies'); await page.waitForTimeout(2500);
const newButtons = await page.locator('main button', { hasText: /^New$/ }).count();
console.log('rows', await page.locator('table[role=grid] tbody tr').count(), '| New offered:', newButtons);
await page.keyboard.press('Alt+n'); await page.waitForTimeout(800);
const form = await page.locator('.record-form').count();
console.log('Alt+N opens a new company form:', form > 0);
if (form > 0) {
  await page.keyboard.type('Critic No Company Trading LLC');
  await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
  console.log('after Ctrl+S, message:', JSON.stringify((await page.locator('.record-form [role=alert], .record-form .error, [role=alert]').allTextContents()).join(' | ').slice(0, 300)));
}
await page.screenshot({ path: '/home/shan/evidence-staging/p02-tenancy/r10/' + shotName, type: 'jpeg', quality: 60 });
await browser.close();
// Round 10: the palette's new "New company" action for the same user.
{
  const b2 = await chromium.launch({ executablePath: `${process.env.HOME}/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome` });
  const p2 = await (await b2.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
  await p2.goto(base + '/'); await p2.locator('input[name="email"]:focus').waitFor();
  await p2.keyboard.type(email); await p2.keyboard.press('Tab'); await p2.keyboard.type(pw); await p2.keyboard.press('Enter');
  await p2.locator('main h1').waitFor(); await p2.waitForTimeout(800);
  await p2.keyboard.press('Control+k'); await p2.waitForTimeout(300); await p2.keyboard.type('new compan'); await p2.waitForTimeout(900);
  console.log('palette options for "new compan":', JSON.stringify((await p2.locator('[role=option]').allTextContents()).slice(0, 6)));
  await b2.close();
}
