// Critic p04 r7: use the shell as a user would, in English and Arabic, by keyboard.
const { chromium } = require('/home/shan/critic/p04-shell-r7/gauntlet/compare/node_modules/playwright-core');
const fs = require('fs');
const BASE = process.env.BASE || 'http://localhost:20450';
const OUT = process.env.OUT || '/home/shan/evidence-staging/p04-shell/r7';
const PW = 'Demo-Pass-2026';
const log = {};
const note = (k, v) => { log[k] = v; console.log(k, typeof v === 'string' ? v : JSON.stringify(v)); };
const shot = async (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 60 });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

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

// Latin words visible on an Arabic screen (hard-coded English candidates), minus e-mails, codes and digits.
const latinOnScreen = () => {
  const out = new Set();
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  let n;
  while ((n = walker.nextNode())) {
    const el = n.parentElement;
    if (!el || el.closest('[hidden],script,style,input,textarea') || el.offsetParent === null) continue;
    const text = n.textContent;
    for (const w of text.split(/\s+/)) {
      if (/[A-Za-z]{3,}/.test(w) && !/@|\.example|^[A-Z0-9\-·_]+$/.test(w)) out.add(`${w} <${el.tagName.toLowerCase()}${el.className ? '.' + String(el.className).split(' ')[0] : ''}>`);
    }
  }
  for (const el of document.querySelectorAll('[aria-label],[title],[placeholder]')) {
    for (const a of ['aria-label', 'title', 'placeholder']) {
      const v = el.getAttribute(a);
      if (v && /[A-Za-z]{3,}/.test(v) && !/@|\.example/.test(v)) out.add(`${a}="${v}" <${el.tagName.toLowerCase()}>`);
    }
  }
  return [...out];
};

(async () => {
  const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
  const errors = [];
  // 1. First screen in Arabic (browser locale ar-AE), keyboard only sign-in in Arabic.
  {
    const ctx = await browser.newContext({ locale: 'ar-AE', viewport: { width: 1366, height: 768 }, timezoneId: 'Asia/Dubai' });
    const page = await ctx.newPage();
    page.on('pageerror', (e) => errors.push(String(e)));
    await page.goto(BASE + '/');
    await page.locator('input[name="email"]').waitFor();
    note('signin.ar', await page.evaluate(() => ({ dir: document.documentElement.dir, lang: document.documentElement.lang, title: document.title, focused: document.activeElement?.getAttribute('name') })));
    note('signin.ar.latin', await page.evaluate(latinOnScreen));
    await shot(page, '01-sign-in-ar');
    await ctx.close();
  }
  // 2. English admin.
  const ctx = await browser.newContext({ locale: 'en-US', viewport: { width: 1366, height: 768 }, timezoneId: 'Asia/Dubai' });
  const page = await ctx.newPage();
  page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
  page.on('pageerror', (e) => errors.push(String(e)));
  await signInKeys(page, 'admin@alnoor.example');
  note('home.en', await page.evaluate(() => ({ dir: document.documentElement.dir, title: document.title, path: location.pathname, nav: [...document.querySelectorAll('nav.navpane a')].map((a) => a.textContent.trim()), breadcrumbs: document.querySelector('nav[aria-label*="read" i], .breadcrumbs')?.textContent, status: document.querySelector('footer')?.textContent })));
  await shot(page, '02-home-en');

  // Tab order with focus rings.
  await page.evaluate(() => document.activeElement && document.activeElement.blur());
  const tabs = [];
  for (let i = 0; i < 20; i++) {
    await page.keyboard.press('Tab');
    tabs.push(await page.evaluate(() => {
      const e = document.activeElement;
      const cs = getComputedStyle(e);
      return `${e.tagName.toLowerCase()} "${(e.getAttribute('aria-label') || e.textContent || '').trim().slice(0, 30)}" outline=${cs.outlineStyle}/${cs.outlineWidth}/${cs.outlineColor} shadow=${cs.boxShadow.slice(0, 30)}`;
    }));
  }
  note('tab-order.en', tabs);

  // Palette: records of every kind.
  const palette = async (q) => {
    await page.keyboard.press('Control+k');
    await page.locator('[role=dialog] input[role=combobox]').waitFor();
    await page.keyboard.type(q);
    await sleep(900);
    const r = await page.evaluate(() => [...document.querySelectorAll('[role=dialog] [role=option]')].map((o) => o.textContent.trim().replace(/\s+/g, ' ').slice(0, 70)).slice(0, 14));
    return r;
  };
  for (const q of ['Administrator', 'Read-only', 'ALN-DXB', 'Jebel', 'mariam', 'new user', 'arabic', 'print', 'المستخدمون', 'shortcuts', 'sign out', 'dark', 'language']) {
    note(`palette.en:${q}`, await palette(q));
    if (q === 'ALN-DXB') await shot(page, '03-palette-en');
    await page.keyboard.press('Escape');
    await sleep(150);
  }
  // Open a role from the palette.
  await page.keyboard.press('Control+k');
  await page.keyboard.type('Read-only');
  await sleep(900);
  // move to the first role record
  const opts = await page.evaluate(() => [...document.querySelectorAll('[role=dialog] [role=option]')].map((o) => o.textContent.trim()));
  const idx = opts.findIndex((o) => /Read-only/.test(o) && !/Users|screen/i.test(o));
  for (let i = 0; i < Math.max(0, idx); i++) await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await sleep(1200);
  note('palette.open-role', await page.evaluate(() => ({ path: location.pathname + location.search, aside: (document.querySelector('aside, [role=region][aria-label]')?.innerText || '').slice(0, 200), focus: document.activeElement?.tagName + ' ' + (document.activeElement?.getAttribute('aria-label') || document.activeElement?.textContent || '').slice(0, 40) })));

  // Shortcut help.
  await page.keyboard.press('Escape');
  await page.evaluate(() => document.activeElement && document.activeElement.blur());
  await page.keyboard.press('Shift+?');
  await sleep(500);
  note('help.en', await page.evaluate(() => (document.querySelector('[role=dialog]')?.innerText || 'NO DIALOG').slice(0, 1500)));
  await shot(page, '04-shortcut-help-en');
  await page.keyboard.press('Escape');
  await sleep(200);
  note('help.closed-focus', await page.evaluate(() => document.activeElement?.tagName + ' ' + (document.activeElement?.textContent || '').slice(0, 30)));

  // Users list in English, then Alt+L to Arabic.
  await page.goto(BASE + '/identity/users');
  await page.locator('main table tbody tr').first().waitFor();
  await sleep(500);
  let t0 = Date.now();
  await page.keyboard.press('Alt+l');
  await page.waitForFunction(() => document.documentElement.dir === 'rtl');
  note('altL.ms', Date.now() - t0);
  await sleep(800);
  note('users.ar', await page.evaluate(() => {
    const nav = document.querySelector('nav.navpane').getBoundingClientRect();
    const main = document.querySelector('main').getBoundingClientRect();
    return { dir: document.documentElement.dir, lang: document.documentElement.lang, title: document.title, navLeft: nav.left, mainLeft: main.left, navRightOfMain: nav.left > main.left, font: getComputedStyle(document.body).fontFamily, headers: [...document.querySelectorAll('main thead th')].map((t) => t.textContent.trim()), count: document.querySelector('main h1')?.parentElement?.textContent?.slice(0, 120), firstRow: document.querySelector('main tbody tr')?.innerText.replace(/\s+/g, ' ') };
  }));
  note('users.ar.latin', await page.evaluate(latinOnScreen));
  await shot(page, '05-users-ar');
  // Persistence: reload in a new context.
  const prefs = await page.evaluate(async () => (await fetch('/api/identity/me', { headers: { 'X-Erp-Request': '1' } })).json().catch(() => null));
  note('me.after-altL', prefs);
  await page.reload();
  await page.locator('nav.navpane').waitFor();
  await sleep(500);
  note('reload.dir', await page.evaluate(() => document.documentElement.dir));

  // Palette in Arabic, record open.
  await page.keyboard.press('Control+k');
  await page.keyboard.type('مدير');
  await sleep(900);
  note('palette.ar:مدير', await page.evaluate(() => [...document.querySelectorAll('[role=dialog] [role=option]')].map((o) => o.textContent.trim().replace(/\s+/g, ' ').slice(0, 70)).slice(0, 10)));
  await shot(page, '06-palette-ar');
  await page.keyboard.press('Escape');

  // Help in Arabic
  await page.evaluate(() => document.activeElement && document.activeElement.blur());
  await page.keyboard.press('Shift+?');
  await sleep(500);
  note('help.ar.latin', await page.evaluate(latinOnScreen));
  await page.keyboard.press('Escape');

  // Every screen in Arabic: Latin text left over.
  for (const p of ['/', '/identity/users', '/identity/roles', '/tenancy/companies', '/tenancy/branches', '/tenancy/access', '/tenancy/workspace', '/reports', '/me']) {
    await page.goto(BASE + p);
    await page.locator('nav.navpane').waitFor();
    await sleep(900);
    note(`screen.ar:${p}`, { path: await page.evaluate(() => location.pathname), h1: await page.evaluate(() => document.querySelector('main h1')?.textContent), latin: await page.evaluate(latinOnScreen) });
  }
  // Record form in Arabic: open first user by keyboard.
  await page.goto(BASE + '/identity/users');
  await page.locator('main table tbody tr').first().waitFor();
  await sleep(400);
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await sleep(1500);
  note('user-record.ar', await page.evaluate(() => ({ path: location.pathname, aside: (document.querySelector('aside')?.innerText || '').slice(0, 600) })));
  note('user-record.ar.latin', await page.evaluate(latinOnScreen));
  await shot(page, '07-user-record-ar');

  // Print layout in Arabic: print media render of the users list.
  await page.keyboard.press('Escape');
  await page.emulateMedia({ media: 'print' });
  await sleep(500);
  await shot(page, '08-print-media-users-ar');
  await page.emulateMedia({ media: 'screen' });

  // Arabic-Indic digits preference.
  await page.keyboard.press('Control+k');
  await page.keyboard.type('digits');
  await sleep(700);
  note('palette.ar:digits', await page.evaluate(() => [...document.querySelectorAll('[role=dialog] [role=option]')].map((o) => o.textContent.trim().replace(/\s+/g, ' ').slice(0, 70)).slice(0, 6)));
  await page.keyboard.press('Escape');

  // Mobile width, Arabic.
  await page.setViewportSize({ width: 390, height: 800 });
  await page.goto(BASE + '/identity/users');
  await page.locator('nav.navpane, main').first().waitFor();
  await sleep(800);
  note('mobile.ar', await page.evaluate(() => ({ scrollW: document.documentElement.scrollWidth, clientW: document.documentElement.clientWidth })));
  await shot(page, '09-users-ar-390');
  await page.setViewportSize({ width: 1366, height: 768 });

  // Back to English for the next run.
  await page.goto(BASE + '/');
  await page.locator('nav.navpane').waitFor();
  await page.keyboard.press('Alt+l');
  await page.waitForFunction(() => document.documentElement.dir === 'ltr');
  note('errors', errors);
  fs.writeFileSync(`${OUT}/explore.json`, JSON.stringify(log, null, 1));
  await browser.close();
})().catch((e) => { console.error(e); fs.writeFileSync(`${OUT}/explore.json`, JSON.stringify({ ...log, fatal: String(e) }, null, 1)); process.exit(1); });
