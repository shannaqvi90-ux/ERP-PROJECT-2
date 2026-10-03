import { chromium } from 'playwright-core';
const B = 'http://localhost:20350';
const OUT = '/home/user/evidence-staging/p03-identity/r2';
const log = []; const note = (...a) => { const s = a.join(' '); log.push(s); console.log(s); };
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const H = { 'X-Erp-Request': '1' };
async function session(email, password = 'Demo-Pass-2026', locale = 'en-US') {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale });
  const page = await ctx.newPage();
  await page.goto(B + '/');
  await page.locator('input[name="email"]').fill(email);
  await page.locator('input[name="password"]').fill(password);
  await page.keyboard.press('Enter');
  await page.locator('nav[aria-label]').first().waitFor();
  return { ctx, page };
}
const shot = (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 55 });
const { ctx, page } = await session('admin@alnoor.example');
const api = page.request;
// a user to lock out, deactivate, reset
const email = `critic.lock.${Date.now() % 100000}@alnoor.example`;
const roles = (await (await api.get(B + '/api/identity/roles')).json()).items;
const ro = roles.find(r => r.nameEn.startsWith('Read-only'));
const created = await api.post(B + '/api/identity/users', { headers: H, data: { email, displayName: 'Critic Lock', language: 'en', password: 'Critic-Pass-2026x', mustChangePassword: false, roleIds: [ro.id] } });
const user = await created.json(); note('created', created.status(), user.id);
// 7 failures from this client
const statuses = [];
for (let i = 0; i < 7; i++) { const r = await browser.newContext().then(c => c.request.post(B + '/api/auth/sign-in', { headers: H, data: { email, password: 'wrong-' + i } })); statuses.push(r.status()); }
note('wrong passwords:', statuses.join(','));
const right = await (await browser.newContext()).request.post(B + '/api/auth/sign-in', { headers: H, data: { email, password: 'Critic-Pass-2026x' } });
note('right password after failures:', right.status(), (await right.text()).slice(0, 160));
// open in UI
await page.locator('nav a[href="/identity/users"]').first().click();
await page.locator('main table tbody tr').first().waitFor();
await page.keyboard.type(email);
await page.locator('main table tbody tr', { hasText: email }).first().waitFor();
await page.keyboard.press('Enter');
await page.locator('aside h2').waitFor({ timeout: 5000 }).catch(async () => { note('Enter did not open the single match; clicking'); await page.locator('main table tbody tr', { hasText: email }).first().click(); });
await page.locator('aside h2').waitFor();
await page.getByRole('tab', { name: 'What they can do' }).click();
await page.waitForTimeout(700);
note('access tab text:', (await page.locator('aside').innerText()).replace(/\s+/g, ' ').slice(0, 500));
await shot(page, '02-user-access-en');
await page.getByRole('tab', { name: 'Sign-in history' }).click();
await page.waitForTimeout(700);
note('history tab text:', (await page.locator('aside').innerText()).replace(/\s+/g, ' ').slice(0, 600));
await shot(page, '10-sign-in-history-paused-en');
const unblock = page.getByRole('button', { name: /allow|unblock/i });
note('unblock button:', await unblock.count());
if (await unblock.count()) { await unblock.first().click(); await page.waitForTimeout(700); }
const right2 = await (await browser.newContext()).request.post(B + '/api/auth/sign-in', { headers: H, data: { email, password: 'Critic-Pass-2026x' } });
note('right password after unblock:', right2.status());
// deactivate through UI
await page.getByRole('tab', { name: 'Details' }).click();
await page.locator('aside label.id-check input[type=checkbox]').uncheck();
await page.keyboard.press('Control+Enter');
await page.waitForTimeout(800);
const right3 = await (await browser.newContext()).request.post(B + '/api/auth/sign-in', { headers: H, data: { email, password: 'Critic-Pass-2026x' } });
note('sign-in after deactivation:', right3.status());
// reset password via UI
const rp = page.getByRole('button', { name: 'Reset password…' });
note('reset button visible:', await rp.count());
// language per user: user's language field exists
note('language select value:', await page.locator('aside select').first().inputValue());
// audit rows
await ctx.close();
const fs = await import('node:fs');
fs.writeFileSync(`${OUT}/lockout-and-history.txt`, log.join('\n') + '\n');
await browser.close();
