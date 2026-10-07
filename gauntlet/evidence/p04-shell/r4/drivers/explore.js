// Critic p04 r4: walk the shell as a user, in English and Arabic, by keyboard.
const { chromium } = require('playwright-core');
const fs = require('fs');
const BASE = process.env.BASE || 'http://localhost:20450';
const OUT = process.env.OUT || '/home/shan/evidence-staging/p04-shell/r4';
const PW = 'Demo-Pass-2026';
const log = [];
const note = (k, v) => { log.push({ k, v }); console.log(k, typeof v === 'string' ? v : JSON.stringify(v)); };
const shot = async (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 65 });

async function signIn(page, email) {
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]').waitFor();
  await page.locator('input[name="email"]').fill(email);
  await page.locator('input[name="password"]').fill(PW);
  await page.keyboard.press('Enter');
  await page.locator('nav.navpane').waitFor();
  await page.waitForLoadState('networkidle');
}

(async () => {
  const browser = await chromium.launch();
  // 1. First screen in Arabic (browser locale ar-AE).
  {
    const ctx = await browser.newContext({ locale: 'ar-AE', viewport: { width: 1366, height: 768 } });
    const page = await ctx.newPage();
    await page.goto(BASE + '/');
    await page.locator('input[name="email"]').waitFor();
    note('signin.ar', await page.evaluate(() => ({ dir: document.documentElement.dir, lang: document.documentElement.lang, title: document.title, focused: document.activeElement?.getAttribute('name'), h1: document.querySelector('h1')?.textContent })));
    await shot(page, '01-sign-in-ar');
    await ctx.close();
  }
  // 2. English admin: home, palette, help, keyboard.
  const ctx = await browser.newContext({ locale: 'en-US', viewport: { width: 1366, height: 768 }, timezoneId: 'Asia/Dubai' });
  const page = await ctx.newPage();
  const errors = [];
  page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
  page.on('pageerror', (e) => errors.push(String(e)));
  await signIn(page, 'admin@alnoor.example');
  note('home.en', await page.evaluate(() => ({ dir: document.documentElement.dir, title: document.title, path: location.pathname, nav: [...document.querySelectorAll('nav.navpane a')].map((a) => a.textContent.trim()), status: document.querySelector('footer.statusbar')?.textContent })));
  await shot(page, '02-home-en');

  // Tab order from the start of the page: what gets focus, with focus ring.
  await page.keyboard.press('Home');
  await page.evaluate(() => document.activeElement && document.activeElement.blur());
  const tabs = [];
  for (let i = 0; i < 25; i++) {
    await page.keyboard.press('Tab');
    tabs.push(await page.evaluate(() => {
      const e = document.activeElement;
      const cs = getComputedStyle(e);
      return `${e.tagName.toLowerCase()}${e.getAttribute('href') ? '[' + e.getAttribute('href') + ']' : ''} "${(e.getAttribute('aria-label') || e.textContent || '').trim().slice(0, 30)}" outline=${cs.outlineStyle}/${cs.outlineWidth}`;
    }));
  }
  note('tab-order.en', tabs);

  // Reach "Roles" by palette.
  let t0 = Date.now();
  await page.keyboard.press('Control+k');
  await page.locator('[role=dialog].palette').waitFor();
  await page.keyboard.type('rol');
  await page.waitForTimeout(300);
  note('palette.rol.options', await page.locator('[role=option]').allTextContents());
  await shot(page, '03-palette-en');
  await page.keyboard.press('Enter');
  await page.waitForURL('**/identity/roles');
  note('palette.to.roles.ms', Date.now() - t0);
  note('roles.title', await page.title());
  note('focus.after.nav', await page.evaluate(() => document.activeElement?.tagName + ' ' + (document.activeElement?.id || document.activeElement?.className)));

  // Palette record search: a user name.
  await page.keyboard.press('Control+k');
  await page.keyboard.type('mariam');
  await page.locator('[role=option]', { hasText: '@' }).first().waitFor({ timeout: 10000 }).catch(() => {});
  note('palette.mariam', await page.locator('[role=option]').allTextContents());
  await page.keyboard.press('Escape');

  // Palette: can you search roles / companies / branches by name? (records beyond users)
  for (const q of ['Administrator', 'ALN-DXB', 'Deira', 'Read-only']) {
    await page.keyboard.press('Control+k');
    await page.keyboard.type(q);
    await page.waitForTimeout(900);
    note(`palette.${q}`, await page.locator('[role=option]').allTextContents());
    await page.keyboard.press('Escape');
  }

  // Shortcut help.
  await page.locator('main').click({ position: { x: 5, y: 5 } }).catch(() => {});
  await page.keyboard.press('Shift+Slash');
  await page.getByRole('dialog').waitFor();
  note('help.rows', await page.getByRole('dialog').locator('kbd').count());
  note('help.text', (await page.getByRole('dialog').textContent()).slice(0, 1500));
  await shot(page, '04-shortcut-help-en');
  await page.keyboard.press('Escape');

  // Users list by keyboard: Alt+M, Down to Users? Enter, then rows.
  await page.goto(BASE + '/identity/users');
  await page.locator('main table tbody tr').first().waitFor();
  note('users.title.en', await page.title());
  note('users.footer.visibleOnScreen', await page.evaluate(() => [...document.querySelectorAll('.print-footer, .print-letterhead')].map((e) => getComputedStyle(e).display + ' ' + e.textContent.slice(0, 60))));
  note('users.breadcrumbs', await page.locator('nav[aria-label*="read" i], .breadcrumbs').first().textContent().catch(() => null));
  // Numbers/dates on English list
  note('users.cells.sample', await page.locator('main table tbody tr').nth(0).textContent());

  // Switch language with Alt+L on the users list.
  t0 = Date.now();
  await page.keyboard.press('Alt+l');
  await page.waitForFunction(() => document.documentElement.dir === 'rtl');
  note('altL.ms', Date.now() - t0);
  await page.waitForTimeout(500);
  note('users.ar', await page.evaluate(() => ({ title: document.title, lang: document.documentElement.lang, h1: document.querySelector('main h1')?.textContent, nav: getComputedStyle(document.querySelector('nav.navpane')).direction, navRect: document.querySelector('nav.navpane').getBoundingClientRect().x, mainRect: document.querySelector('main').getBoundingClientRect().x, headers: [...document.querySelectorAll('main table thead th')].map((t) => t.textContent.trim()), firstRow: document.querySelector('main table tbody tr')?.textContent, status: document.querySelector('footer.statusbar')?.textContent, font: getComputedStyle(document.querySelector('main h1')).fontFamily })));
  await shot(page, '05-users-ar');

  // Arabic-Indic digits through palette action.
  await page.keyboard.press('Control+k');
  await page.keyboard.type('أرقام');
  await page.waitForTimeout(300);
  note('palette.ar.digits', await page.locator('[role=option]').allTextContents());
  await page.keyboard.press('Enter');
  await page.waitForTimeout(800);
  note('users.ar.arabdigits', await page.evaluate(() => ({ h1: document.querySelector('main h1')?.textContent, count: document.querySelector('main')?.textContent.match(/[٠-٩][٠-٩,٬]*/g)?.slice(0, 5), status: document.querySelector('footer.statusbar')?.textContent })));
  await shot(page, '06-users-ar-arabic-digits');
  // back to latin digits
  await page.keyboard.press('Control+k');
  await page.keyboard.type('أرقام');
  await page.waitForTimeout(300);
  await page.keyboard.press('Enter');
  await page.waitForTimeout(500);

  // Palette in Arabic: find "Users" by Arabic and by English word.
  await page.keyboard.press('Control+k');
  await page.keyboard.type('المستخدم');
  await page.waitForTimeout(300);
  note('palette.ar.users', await page.locator('[role=option]').allTextContents());
  await shot(page, '07-palette-ar');
  await page.keyboard.press('Escape');

  // Print the users screen in Arabic as PDF.
  await page.emulateMedia({ media: 'print' });
  note('print.ar.visible', await page.evaluate(() => ({ topbar: getComputedStyle(document.querySelector('header.topbar')).display, nav: getComputedStyle(document.querySelector('nav.navpane')).display, footer: [...document.querySelectorAll('.print-footer')].map((e) => getComputedStyle(e).display), buttons: [...document.querySelectorAll('main button')].filter((b) => getComputedStyle(b).display !== 'none' && b.offsetParent !== null).map((b) => b.textContent.trim()).slice(0, 10) })));
  await page.pdf({ path: `${OUT}/print-users-ar.pdf`, format: 'A4' });
  await page.emulateMedia({ media: 'screen' });

  // Breadcrumb chevron direction and icon mirroring in RTL.
  note('rtl.icons', await page.evaluate(() => [...document.querySelectorAll('svg')].slice(0, 30).map((s) => ({ cls: s.getAttribute('class'), tf: getComputedStyle(s).transform })).filter((x) => x.tf !== 'none')));

  // Switch back with Alt+L, check title.
  await page.keyboard.press('Alt+l');
  await page.waitForFunction(() => document.documentElement.dir === 'ltr');
  note('users.back.en.title', await page.title());

  // Keyboard into a record: Alt+M > Users > Enter > Down x3 > Enter
  await page.goto(BASE + '/');
  await page.locator('nav.navpane').waitFor();
  await page.keyboard.press('Alt+m');
  note('altM.focus', await page.evaluate(() => document.activeElement?.getAttribute('href')));
  await page.keyboard.press('Escape');

  // Narrow window, Arabic
  await page.setViewportSize({ width: 390, height: 800 });
  await page.goto(BASE + '/identity/users');
  await page.locator('main table tbody tr').first().waitFor();
  note('narrow', await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, cw: document.documentElement.clientWidth })));
  await page.setViewportSize({ width: 1366, height: 768 });

  note('console.errors', errors);
  fs.writeFileSync(`${OUT}/explore.json`, JSON.stringify(log, null, 1));
  await browser.close();
})().catch((e) => {
  console.error(e);
  fs.writeFileSync(`${OUT}/explore.json`, JSON.stringify(log, null, 1));
  process.exit(1);
});
