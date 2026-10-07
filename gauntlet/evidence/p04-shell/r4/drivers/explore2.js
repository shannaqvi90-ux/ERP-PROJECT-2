// Critic p04 r4: print in Arabic, focus behaviour, no-access, preferences, keyboard-only flows.
const { chromium } = require('playwright-core');
const fs = require('fs');
const BASE = process.env.BASE || 'http://localhost:20450';
const OUT = process.env.OUT || '/home/shan/evidence-staging/p04-shell/r4';
const PW = 'Demo-Pass-2026';
const log = [];
const note = (k, v) => { log.push({ k, v }); console.log(k, typeof v === 'string' ? v : JSON.stringify(v)); };
const shot = async (page, name, opts = {}) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 65, ...opts });
const active = (page) => page.evaluate(() => { const e = document.activeElement; return e ? `${e.tagName.toLowerCase()}#${e.id}.${e.className} "${(e.getAttribute('aria-label') || e.textContent || '').trim().slice(0, 40)}"` : null; });

async function signInKeys(page, email) {
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email);
  await page.keyboard.press('Tab');
  await page.keyboard.type(PW);
  await page.keyboard.press('Enter');
  await page.locator('nav.navpane').waitFor();
  await page.waitForLoadState('networkidle');
}

(async () => {
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ locale: 'en-US', viewport: { width: 1366, height: 768 }, timezoneId: 'Asia/Dubai' });
  const page = await ctx.newPage();

  // Sign-in screen: Alt+L before signing in.
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.press('Alt+l');
  await page.waitForTimeout(300);
  note('signin.altL', await page.evaluate(() => ({ dir: document.documentElement.dir, h1: document.querySelector('h1')?.textContent, labels: [...document.querySelectorAll('label')].map((l) => l.textContent.trim()) })));
  await page.keyboard.press('Alt+l');

  // Arabic admin.
  await signInKeys(page, 'admin.ar@alnoor.example');
  note('ar.home', await page.evaluate(() => ({ dir: document.documentElement.dir, title: document.title, h1: document.querySelector('main h1')?.textContent })));

  // Preferences dialog by keyboard and focus return.
  await page.keyboard.press('Alt+p');
  await page.getByRole('dialog').waitFor();
  note('prefs.focus', await active(page));
  note('prefs.text', (await page.getByRole('dialog').innerText()).slice(0, 500));
  await shot(page, '08-preferences-ar');
  await page.keyboard.press('Escape');
  note('prefs.after.esc.focus', await active(page));

  // Users list in Arabic; print media screenshot of the page.
  await page.keyboard.press('Control+k');
  await page.keyboard.type('مستخدم');
  await page.waitForTimeout(300);
  await page.keyboard.press('Enter');
  await page.locator('main table tbody tr').first().waitFor();
  note('ar.users.focus', await active(page));
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(800);
  note('ar.user.open', await page.evaluate(() => ({ url: location.pathname + location.search, aside: document.querySelector('aside')?.innerText?.slice(0, 300) })));
  note('ar.user.open.focus', await active(page));
  await shot(page, '09-user-record-ar');
  await page.keyboard.press('Escape');
  await page.waitForTimeout(300);
  note('ar.after.esc.focus', await active(page));

  await page.emulateMedia({ media: 'print' });
  await shot(page, '10-print-users-ar', { fullPage: false });
  await page.emulateMedia({ media: 'screen' });

  // Shortcut help in Arabic.
  await page.locator('main h1').click();
  await page.keyboard.press('Shift+Slash');
  await page.getByRole('dialog').waitFor();
  note('ar.help', (await page.getByRole('dialog').innerText()).slice(0, 600));
  await page.keyboard.press('Escape');

  // Not-found page and a no-access page (viewer on roles?).
  await page.goto(BASE + '/no/such/screen');
  await page.waitForTimeout(500);
  note('ar.notfound', await page.evaluate(() => ({ title: document.title, text: document.querySelector('main')?.innerText })));

  // Sign out by keyboard: is there a chord? via palette.
  await page.keyboard.press('Control+k');
  await page.keyboard.type('خروج');
  await page.waitForTimeout(300);
  note('ar.palette.signout', await page.locator('[role=option]').allTextContents());
  await page.keyboard.press('Enter');
  await page.locator('input[name="email"]').waitFor();

  // Viewer: no-access screen by address.
  await signInKeys(page, 'viewer@alnoor.example');
  note('viewer.nav', await page.evaluate(() => [...document.querySelectorAll('nav.navpane a')].map((a) => a.getAttribute('href'))));
  await page.goto(BASE + '/identity/roles');
  await page.waitForTimeout(600);
  note('viewer.roles', await page.evaluate(() => document.querySelector('main')?.innerText));
  await page.goto(BASE + '/tenancy/tenant');
  await page.waitForTimeout(600);
  note('viewer.tenant', await page.evaluate(() => document.querySelector('main')?.innerText?.slice(0, 200)));
  await page.keyboard.press('Control+k');
  await page.waitForTimeout(300);
  note('viewer.palette.empty', await page.locator('[role=option]').allTextContents());
  await page.keyboard.press('Escape');

  fs.writeFileSync(`${OUT}/explore2.json`, JSON.stringify(log, null, 1));
  await browser.close();
})().catch((e) => {
  console.error(e);
  fs.writeFileSync(`${OUT}/explore2.json`, JSON.stringify(log, null, 1));
  process.exit(1);
});
