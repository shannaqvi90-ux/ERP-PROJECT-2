// Critic p03 r7 walk 2: create a user with a restricted role from the keyboard, then sign in as them.
import { open, signIn, BASE, shot, focused } from './pw.mjs';
const T = Math.random().toString(16).slice(2, 8);
const { browser, page, errors } = await open(1600, 900);
await signIn(page, 'admin@alnoor.example');
await page.goto(BASE + '/identity/users'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(600);
await page.keyboard.press('Alt+n'); await page.waitForTimeout(1000);
console.log('focused after Alt+N:', await focused(page));
const keys = [];
async function type(s) { await page.keyboard.type(s); keys.push(s.length); }
await type(`ro.${T}@alnoor.example`);
await page.keyboard.press('Tab'); await type(`Read Only ${T}`);
const panelText = await page.locator('aside, [role=complementary], [role=dialog]').last().innerText();
console.log('NEW USER PANEL:\n' + panelText.slice(0, 1500));
// find the role filter field and choose "Read-only"
const roleFilter = page.getByPlaceholder(/find a role|filter roles|search roles/i).first();
console.log('role filter fields:', await roleFilter.count());
if (await roleFilter.count()) { await roleFilter.focus(); await type('Read-only'); await page.keyboard.press('Enter'); }
else { await page.getByLabel(/^Read-only/).first().check(); }
await page.waitForTimeout(400);
await shot(page, '02-new-user-restricted-role-en');
await page.keyboard.press('Control+Enter').catch(() => {});
await page.waitForTimeout(1500);
let after = await page.locator('aside, [role=complementary], [role=dialog]').last().innerText();
if (!/set-up code|setup code/i.test(after)) { await page.getByRole('button', { name: /^(Create|Save|Invite)/ }).first().click(); await page.waitForTimeout(1500); after = await page.locator('aside, [role=complementary], [role=dialog]').last().innerText(); }
console.log('AFTER CREATE:\n' + after.slice(0, 1200));
const code = (after.match(/[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}/) || [])[0];
console.log('set-up code', code);
await shot(page, '03-new-user-setup-code-en');
console.log('errors', errors);
await browser.close();
// The new user signs in with the code, must choose a password, then sees only read screens.
const s = await open(1600, 900);
await signIn(s.page, `ro.${T}@alnoor.example`, code);
console.log('after code sign-in:\n' + (await s.page.locator('body').innerText()).slice(0, 600));
const pw = s.page.locator('input[type=password]');
console.log('password fields:', await pw.count());
if (await pw.count() >= 2) { await pw.nth(0).fill('Critic-New-Pass-2026'); await pw.nth(1).fill('Critic-New-Pass-2026'); await s.page.keyboard.press('Enter'); await s.page.waitForTimeout(1500); }
else if (await pw.count() === 1) { await pw.nth(0).fill('Critic-New-Pass-2026'); await s.page.keyboard.press('Enter'); await s.page.waitForTimeout(1500); }
await s.page.goto(BASE + '/identity/users'); await s.page.waitForLoadState('networkidle'); await s.page.waitForTimeout(1000);
console.log('RO users screen:\n' + (await s.page.locator('body').innerText()).slice(0, 700));
console.log('New user button:', await s.page.getByRole('button', { name: /new user/i }).count());
await s.page.keyboard.press('ArrowDown'); await s.page.keyboard.press('Enter'); await s.page.waitForTimeout(1200);
const p2 = s.page.locator('aside, [role=complementary], [role=dialog]').last();
console.log('RO panel:\n' + (await p2.innerText()).slice(0, 600));
console.log('RO editable inputs in panel:', await p2.locator('input:not([disabled]):not([readonly]), select:not([disabled]), textarea:not([readonly])').count(), 'Save buttons:', await p2.getByRole('button', { name: /^save/i }).count());
await shot(s.page, '04-read-only-user-panel-en');
const r = await s.page.evaluate(async () => { const x = await fetch('/api/identity/roles', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ nameEn: 'x', nameAr: 'x', permissions: [] }) }); return x.status; });
console.log('RO POST /api/identity/roles from the browser:', r);
console.log('errors', s.errors);
await s.browser.close();
