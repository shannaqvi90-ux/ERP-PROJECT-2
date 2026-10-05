// Browser walk: sign-in screen and empty shell, English and Arabic, keyboard only.
import { chromium } from '/home/user/critic/p00-foundation-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = process.env.BASE || 'http://localhost:20050';
const OUT = '/home/user/evidence-staging/p00-foundation/r4';
const PW = 'Demo-Pass-2026';
const log = (...a) => console.log(...a);
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
async function walk(locale, email, tag, n0) {
  const ctx = await browser.newContext({ locale, viewport: { width: 1366, height: 768 } });
  const page = await ctx.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push('pageerror ' + e.message));
  await page.goto(BASE + '/');
  await page.waitForLoadState('networkidle');
  const first = await page.evaluate(() => ({ lang: document.documentElement.lang, dir: document.documentElement.dir, title: document.title, focus: document.activeElement?.outerHTML?.slice(0, 160) }));
  log(tag, 'first screen', JSON.stringify(first));
  await page.screenshot({ path: `${OUT}/${n0}-signin-${tag}.jpg`, type: 'jpeg', quality: 60 });
  // Wrong password first, to see the error in this language.
  const t0 = Date.now();
  await page.keyboard.type(email);
  await page.keyboard.press('Tab');
  await page.keyboard.type('wrong-password');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1500);
  const err = await page.evaluate(() => document.querySelector('[role=alert]')?.textContent || '');
  log(tag, 'bad password message:', err);
  // Now real password: field focus after failure?
  const focusAfter = await page.evaluate(() => document.activeElement?.getAttribute('type') + ' ' + document.activeElement?.getAttribute('name'));
  log(tag, 'focus after failure', focusAfter);
  await page.locator('input[type=password]').fill('');
  await page.locator('input[type=password]').focus();
  const t1 = Date.now();
  await page.keyboard.type(PW);
  await page.keyboard.press('Enter');
  await page.getByRole('button', { name: /^(Sign out|تسجيل الخروج)$/ }).waitFor({ timeout: 20000 }).catch(e => log(tag, 'no sign-out button', e.message));
  await page.waitForLoadState('networkidle');
  log(tag, 'sign-in ms', Date.now() - t1);
  const shell = await page.evaluate(() => ({ url: location.pathname, lang: document.documentElement.lang, dir: document.documentElement.dir, title: document.title, nav: [...document.querySelectorAll('nav a, [role=navigation] a')].map(a => a.textContent.trim()).slice(0, 15), text: document.body.innerText.slice(0, 400).replace(/\n+/g, ' | ') }));
  log(tag, 'shell', JSON.stringify(shell));
  await page.screenshot({ path: `${OUT}/${n0 + 1}-shell-${tag}.jpg`, type: 'jpeg', quality: 60 });
  // Keyboard: Tab through first 12 focus stops.
  const stops = [];
  for (let i = 0; i < 12; i++) {
    await page.keyboard.press('Tab');
    stops.push(await page.evaluate(() => { const e = document.activeElement; return (e?.tagName || '') + ':' + (e?.getAttribute('aria-label') || e?.textContent || '').trim().slice(0, 30); }));
  }
  log(tag, 'tab stops', JSON.stringify(stops));
  log(tag, 'console errors', JSON.stringify(errors));
  return { ctx, page };
}
const en = await walk('en-US', 'admin@alnoor.example', 'en', 1);
const ar = await walk('ar-AE', 'admin.ar@alnoor.example', 'ar', 3);
// Sign out by keyboard-less route: find the sign-out control
for (const [tag, { page }] of [['en', en], ['ar', ar]]) {
  const before = await page.evaluate(async () => (await fetch('/api/auth/session')).status);
  const btn = page.getByRole('button', { name: /sign out|تسجيل الخروج|خروج/i });
  const menu = page.getByRole('button', { name: /account|user|حساب|admin|المستخدم/i });
  log(tag, 'signout buttons visible', await btn.count(), 'menu', await menu.count());
}
// noaccess user sees nothing
{
  const ctx = await browser.newContext({ locale: 'en-US' });
  const page = await ctx.newPage();
  await page.goto(BASE + '/');
  await page.waitForLoadState('networkidle');
  await page.keyboard.type('noaccess@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type(PW); await page.keyboard.press('Enter');
  await page.waitForTimeout(2500);
  await page.goto(BASE + '/identity/users'); await page.waitForTimeout(2000);
  log('noaccess', await page.evaluate(() => document.body.innerText.slice(0, 300).replace(/\n+/g, ' | ')));
  await page.screenshot({ path: `${OUT}/05-noaccess-en.jpg`, type: 'jpeg', quality: 60 });
}
await browser.close();
