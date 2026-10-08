// Critic browser walk (p00 r3): sign-in, shell, English/Arabic RTL, keyboard, no-access, sign-out.
import { chromium } from '/home/user/critic/p00-foundation-r3/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20050';
const OUT = '/home/user/evidence-staging/p00-foundation/r3';
const PW = 'Demo-Pass-2026';
const log = [];
const note = (k, v) => { log.push([k, v]); console.log(k, typeof v === 'string' ? v : JSON.stringify(v)); };
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });

async function ctxFor(locale) {
  const context = await browser.newContext({ locale, viewport: { width: 1366, height: 800 }, timezoneId: 'Asia/Dubai' });
  const page = await context.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push('pageerror ' + e.message));
  return { context, page, errors };
}
const dirInfo = page => page.evaluate(() => ({ lang: document.documentElement.lang, dir: document.documentElement.dir, title: document.title, focused: document.activeElement?.getAttribute('name') || document.activeElement?.tagName }));

async function keyboardSignIn(page, email) {
  const t0 = Date.now();
  await page.keyboard.type(email);
  await page.keyboard.press('Tab');
  await page.keyboard.type(PW);
  await page.keyboard.press('Enter');
  await page.locator('#navpane, header').first().waitFor({ state: 'attached', timeout: 30000 }); await page.waitForFunction(() => !document.querySelector('input[name="password"]'), null, { timeout: 30000 });
  return Date.now() - t0;
}

// 1. English sign-in screen
{
  const { context, page, errors } = await ctxFor('en-US');
  await page.goto(BASE + '/');
  await page.locator('input:focus').waitFor();
  note('en sign-in screen', await dirInfo(page));
  await page.screenshot({ path: `${OUT}/01-signin-en.jpg`, type: 'jpeg', quality: 70 });
  // empty submit and bad password
  await page.keyboard.press('Enter');
  note('empty submit errors', await page.locator('[id$="-error"], [role="alert"]').allInnerTexts());
  await page.keyboard.type('admin@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('wrong-password-1'); await page.keyboard.press('Enter');
  await page.waitForTimeout(2500);
  note('bad password message', await page.locator('[role="alert"], #signin-error').allInnerTexts());
  note('focus after failed sign-in', await dirInfo(page));
  await page.screenshot({ path: `${OUT}/02-signin-wrong-password-en.jpg`, type: 'jpeg', quality: 70 });
  // language toggle on the sign-in screen
  const toggle = page.getByRole('button', { name: /العربية|Switch language/ }).first();
  note('sign-in language toggle present', await toggle.count());
  if (await toggle.count()) {
    await toggle.click();
    await page.waitForTimeout(500);
    note('after toggle', await dirInfo(page));
    await page.screenshot({ path: `${OUT}/03-signin-ar-rtl.jpg`, type: 'jpeg', quality: 70 });
  }
  note('console errors (en sign-in)', errors);
  await context.close();
}

// 2. Arabic locale from the first screen, keyboard sign-in of the Arabic admin
{
  const { context, page, errors } = await ctxFor('ar-AE');
  await page.goto(BASE + '/');
  await page.locator('input:focus').waitFor();
  note('ar-AE first screen', await dirInfo(page));
  const ms = await keyboardSignIn(page, 'admin.ar@alnoor.example');
  note('ar keyboard sign-in ms', ms);
  await page.waitForTimeout(1000);
  note('ar shell', await dirInfo(page));
  note('ar nav text', (await page.locator('nav').first().innerText()).slice(0, 300));
  await page.screenshot({ path: `${OUT}/04-shell-ar-rtl.jpg`, type: 'jpeg', quality: 70 });
  note('console errors (ar)', errors);
  await context.close();
}

// 3. English admin: shell, tab order, sign out by keyboard, back button
{
  const { context, page, errors } = await ctxFor('en-US');
  await page.goto(BASE + '/');
  await page.locator('input:focus').waitFor();
  const ms = await keyboardSignIn(page, 'admin@alnoor.example');
  note('en keyboard sign-in ms', ms);
  await page.waitForTimeout(1000);
  note('en shell', await dirInfo(page));
  note('en nav text', (await page.locator('nav').first().innerText()).slice(0, 400));
  await page.screenshot({ path: `${OUT}/05-shell-en.jpg`, type: 'jpeg', quality: 70 });
  const cookies = await context.cookies();
  note('session cookie flags', cookies.map(c => ({ name: c.name, httpOnly: c.httpOnly, sameSite: c.sameSite, secure: c.secure })));
  // Tab walk
  const tabs = [];
  for (let i = 0; i < 12; i++) { await page.keyboard.press('Tab'); tabs.push(await page.evaluate(() => (document.activeElement?.getAttribute('aria-label') || document.activeElement?.innerText || document.activeElement?.tagName || '').slice(0, 40))); }
  note('tab order', tabs);
  // Sign out
  const so = page.getByRole('button', { name: /Sign out/ }).or(page.getByRole('menuitem', { name: /Sign out/ })).first();
  note('sign-out control visible', await so.count());
  if (await so.count()) { await so.focus(); await page.keyboard.press('Enter'); }
  await page.locator('input[name="email"], input[type="email"]').first().waitFor({ timeout: 15000 }).catch(() => {});
  note('after sign-out url', page.url());
  const s = await page.evaluate(async () => { const r = await fetch('/api/auth/session', { headers: { 'X-Erp-Request': '1' } }); return r.status + ' ' + (await r.text()).slice(0, 120); });
  note('session status after sign-out', s);
  await page.goBack(); await page.waitForTimeout(1000);
  note('back after sign-out shows', { url: page.url(), text: (await page.locator('body').innerText()).slice(0, 160), password: await page.locator('input[name="password"]').count() });
  note('console errors (en)', errors);
  await context.close();
}

// 4. No-access and read-only users
for (const [who, file] of [['noaccess@alnoor.example', '06-noaccess-en.jpg'], ['viewer@alnoor.example', '07-viewer-users-en.jpg']]) {
  const { context, page, errors } = await ctxFor('en-US');
  await page.goto(BASE + '/');
  await page.locator('input:focus').waitFor();
  await keyboardSignIn(page, who);
  await page.waitForTimeout(800);
  note(who + ' nav', (await page.locator('nav').first().innerText()).slice(0, 300));
  await page.goto(BASE + '/identity/users');
  await page.waitForTimeout(2500);
  note(who + ' /identity/users', (await page.locator('main').first().innerText().catch(() => '')).slice(0, 300));
  note(who + ' buttons on users', await page.locator('main button').allInnerTexts().catch(() => []));
  await page.screenshot({ path: `${OUT}/${file}`, type: 'jpeg', quality: 70 });
  note('console errors ' + who, errors);
  await context.close();
}
await browser.close();
import fs from 'node:fs';
fs.writeFileSync(`${OUT}/browser/walk.json`, JSON.stringify(log, null, 1));
