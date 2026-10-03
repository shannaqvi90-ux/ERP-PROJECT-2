import { chromium } from 'playwright-core';
const B = 'http://localhost:20350';
const OUT = '/home/user/evidence-staging/p03-identity/r2';
const log = [];
const note = (...a) => { const s = a.join(' '); log.push(s); console.log(s); };
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const errors = [];
async function session(email, locale = 'en-US') {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale });
  const page = await ctx.newPage();
  page.on('console', m => { if (m.type() === 'error') errors.push(`${email}: ${m.text()}`); });
  page.on('response', r => { if (r.status() >= 500) errors.push(`${email}: ${r.status()} ${r.url()}`); });
  await page.goto(B + '/');
  await page.locator('input[name="email"]').waitFor();
  // keyboard-only sign-in
  await page.locator('input[name="email"]').focus();
  await page.keyboard.type(email);
  await page.keyboard.press('Tab');
  await page.keyboard.type('Demo-Pass-2026');
  await page.keyboard.press('Enter');
  await page.locator('nav[aria-label]').first().waitFor();
  return { ctx, page };
}
const shot = (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 55 });

// ---- English administrator
{
  const { ctx, page } = await session('admin@alnoor.example');
  note('html dir/lang after sign-in (en):', await page.evaluate(() => document.documentElement.dir + '/' + document.documentElement.lang));
  await page.locator('nav a[href="/identity/users"]').first().click();
  await page.locator('main table tbody tr').first().waitFor();
  await page.waitForTimeout(500);
  note('users list count text:', (await page.locator('main').innerText()).match(/[\d,]+\s+users?/i)?.[0]);
  note('focus on open:', await page.evaluate(() => document.activeElement?.getAttribute('aria-label')));
  // search a dataset user by word prefixes
  const t0 = Date.now();
  await page.keyboard.type('maj ani pil');
  await page.locator('main table tbody tr', { hasText: 'Majid' }).first().waitFor({ timeout: 10000 }).catch(() => {});
  note('search "maj ani pil" rows:', await page.locator('main table tbody tr').count(), 'in', Date.now() - t0, 'ms');
  // headers / sort / filter affordances
  note('column headers:', (await page.locator('main table thead th').allInnerTexts()).join(' | '));
  await shot(page, '01-users-list-search-en');
  await page.keyboard.press('Enter');
  await page.locator('aside h2').waitFor({ timeout: 10000 }).catch(() => {});
  note('Enter in search opened:', await page.locator('aside h2').innerText().catch(() => 'nothing'));
  note('user panel fields:', (await page.locator('aside label .field-label, aside label').allInnerTexts()).map(s => s.replace(/\n.*/s, '')).join(' | '));
  note('default company field present:', await page.locator('aside').getByText(/company/i).count());
  await page.getByRole('tab', { name: 'What they can do' }).click().catch(() => note('no access tab'));
  await page.waitForTimeout(600);
  await shot(page, '02-user-access-en');
  await page.getByRole('tab', { name: 'Sign-in history' }).click().catch(() => note('no history tab'));
  await page.waitForTimeout(600);
  // New user via button
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'New user' }).click();
  await page.locator('input[name="email"]').waitFor();
  note('new user form fields:', (await page.locator('aside input, aside select').evaluateAll(els => els.map(e => e.name || e.type + ':' + (e.getAttribute('aria-label') || '')))).join(', '));
  await page.keyboard.type('critic.walk');
  await page.keyboard.press('Tab');
  note('email completed to:', await page.locator('input[name="email"]').inputValue(), 'name suggested:', await page.locator('input[name="displayName"]').inputValue());
  await shot(page, '03-new-user-en');
  await page.keyboard.press('Escape');
  // Roles
  await page.locator('nav a[href="/identity/roles"]').first().click();
  await page.locator('main table tbody tr').first().waitFor();
  await page.locator('main table tbody tr', { hasText: 'Read-only' }).first().click();
  await page.locator('aside').waitFor();
  await page.waitForTimeout(500);
  note('matrix module groups:', (await page.locator('aside th[scope="rowgroup"], aside .matrix-module, aside tbody th').allInnerTexts()).slice(0, 12).join(' | '));
  note('role panel buttons:', (await page.locator('aside button').allInnerTexts()).join(' | '));
  note('company scope control in role/assignment:', await page.locator('aside').getByText(/company/i).count());
  await shot(page, '04-role-matrix-en');
  await ctx.close();
}
// ---- Arabic administrator
{
  const { ctx, page } = await session('admin.ar@alnoor.example', 'ar-AE');
  note('html dir/lang (ar user):', await page.evaluate(() => document.documentElement.dir + '/' + document.documentElement.lang));
  await page.locator('nav a[href="/identity/users"]').first().click();
  await page.locator('main table tbody tr').first().waitFor();
  await page.waitForTimeout(800);
  const latin = await page.locator('main table tbody tr').first().innerText();
  note('first row (ar):', latin.replace(/\s+/g, ' '));
  await shot(page, '05-users-list-ar');
  await page.locator('main table tbody tr').nth(1).click();
  await page.locator('aside h2').waitFor();
  const tabs = await page.locator('aside [role="tab"]').allInnerTexts();
  note('panel tabs (ar):', tabs.join(' | '));
  await page.locator('aside [role="tab"]').nth(1).click();
  await page.waitForTimeout(600);
  await shot(page, '06-user-access-ar');
  if (tabs.length > 2) { await page.locator('aside [role="tab"]').nth(2).click(); await page.waitForTimeout(600); await shot(page, '07-user-history-ar'); }
  // overflow check
  const overflow = await page.evaluate(() => [...document.querySelectorAll('aside *')].filter(e => e.scrollWidth > e.clientWidth + 2 && getComputedStyle(e).overflowX === 'visible').length);
  note('aside elements overflowing horizontally (ar):', overflow);
  await page.locator('nav a[href="/identity/roles"]').first().click();
  await page.locator('main table tbody tr').first().waitFor();
  await page.locator('main table tbody tr').first().click();
  await page.waitForTimeout(800);
  const latinWords = await page.evaluate(() => (document.querySelector('main')?.innerText.match(/\b[A-Za-z]{3,}\b/g) || []).slice(0, 20));
  note('Latin words on Arabic roles screen:', latinWords.join(' '));
  await shot(page, '08-role-matrix-ar');
  await ctx.close();
}
// ---- Read-only user
{
  const { ctx, page } = await session('viewer@alnoor.example');
  const navs = await page.locator('nav a').allInnerTexts();
  note('viewer nav:', navs.join(' | '));
  await page.locator('nav a[href="/identity/roles"]').first().click();
  await page.locator('main table tbody tr').first().waitFor();
  note('viewer sees New role:', await page.getByRole('button', { name: 'New role' }).count());
  await page.locator('main table tbody tr').first().click();
  await page.waitForTimeout(600);
  note('viewer role panel buttons:', (await page.locator('aside button').allInnerTexts()).join(' | '));
  note('viewer enabled matrix checkboxes:', await page.locator('aside input[type=checkbox]:not([disabled])').count());
  await page.locator('nav a[href="/identity/users"]').first().click();
  await page.locator('main table tbody tr').first().waitFor();
  await page.locator('main table tbody tr').first().click();
  await page.waitForTimeout(600);
  note('viewer user panel buttons:', (await page.locator('aside button').allInnerTexts()).join(' | '));
  note('viewer user panel tabs:', (await page.locator('aside [role="tab"]').allInnerTexts()).join(' | '));
  await shot(page, '09-read-only-user-en');
  await ctx.close();
}
note('console errors / 5xx:', errors.length ? errors.join('\n') : 'none');
const fs = await import('node:fs');
fs.writeFileSync(`${OUT}/browser-walk.txt`, log.join('\n') + '\n');
await browser.close();
