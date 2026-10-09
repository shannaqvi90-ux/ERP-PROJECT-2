// Critic p03 r7 walk 2b: the invited read-only user signs in with the set-up code, chooses a password, sees only read screens.
import { open, BASE, shot, focused } from './pw.mjs';
const [email, code] = process.argv.slice(2);
const s = await open(1600, 900);
await s.page.goto(BASE + '/?domain=alnoor.example'); await s.page.waitForLoadState('networkidle');
const e = s.page.locator('input[type=email], input[name=email], input[autocomplete=username]').first();
await e.fill(email); await s.page.keyboard.press('Enter');
const pw = s.page.locator('input[type=password]');
await pw.first().waitFor(); await pw.first().fill(code); await s.page.keyboard.press('Enter'); await s.page.waitForTimeout(1500);
console.log('password fields after code:', await pw.count());
await pw.nth(1).fill('Critic-New-Pass-2026'); await pw.nth(2).fill('Critic-New-Pass-2026'); await s.page.keyboard.press('Enter');
await s.page.waitForLoadState('networkidle'); await s.page.waitForTimeout(1500);
console.log('after choosing password:\n' + (await s.page.locator('body').innerText()).slice(0, 500));
await s.page.goto(BASE + '/identity/users'); await s.page.waitForLoadState('networkidle'); await s.page.waitForTimeout(1200);
console.log('RO users screen:\n' + (await s.page.locator('body').innerText()).slice(0, 500));
console.log('New user button:', await s.page.getByRole('button', { name: /new user/i }).count());
await s.page.keyboard.press('Alt+n'); await s.page.waitForTimeout(800);
console.log('after Alt+N, new-user panel open?', /New user/.test(await s.page.locator('body').innerText()) && await s.page.getByRole('button', { name: /^create$/i }).count());
await s.page.keyboard.press('Escape');
await s.page.locator('main input').first().focus().catch(() => {});
await s.page.keyboard.press('ArrowDown'); await s.page.keyboard.press('Enter'); await s.page.waitForTimeout(1500);
const p2 = s.page.locator('aside, [role=complementary], [role=dialog]').last();
if (await p2.count()) {
  console.log('RO panel:\n' + (await p2.innerText()).slice(0, 500));
  console.log('RO editable inputs in panel:', await p2.locator('input:not([disabled]):not([readonly]), select:not([disabled]), textarea:not([readonly])').count(), 'Save buttons:', await p2.getByRole('button', { name: /^save/i }).count(), 'Reset password:', await p2.getByRole('button', { name: /reset password/i }).count());
}
await shot(s.page, '04-read-only-user-panel-en');
await s.page.goto(BASE + '/identity/roles'); await s.page.waitForLoadState('networkidle'); await s.page.waitForTimeout(1000);
console.log('RO roles screen New role button:', await s.page.getByRole('button', { name: /new role/i }).count(), 'Copy:', await s.page.getByRole('button', { name: /copy/i }).count(), 'Delete:', await s.page.getByRole('button', { name: /delete/i }).count());
const r = await s.page.evaluate(async () => {
  const out = {};
  for (const [m, u, b] of [['POST', '/api/identity/roles', { nameEn: 'x', nameAr: 'x', permissions: [] }], ['POST', '/api/identity/users', { email: 'zz@alnoor.example', displayName: 'z', language: 'en', roleIds: [] }], ['POST', '/api/identity/users/matching/active', { active: false, search: 'admin', expectedCount: 1 }], ['GET', '/api/identity/users?take=1'], ['GET', '/api/tenancy/companies']]) {
    const x = await fetch(u, { method: m, headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: b ? JSON.stringify(b) : undefined }); out[`${m} ${u}`] = x.status;
  }
  return out;
});
console.log('RO API from the browser:', JSON.stringify(r));
console.log('errors', s.errors);
await s.browser.close();
