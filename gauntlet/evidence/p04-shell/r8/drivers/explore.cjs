// Critic p04 r8: browser exploration of the shell (English, Arabic, keyboard).
const { chromium } = require('/home/shan/critic/p04-shell-r8/gauntlet/compare/node_modules/playwright-core');
const BASE = process.env.BASE || 'http://localhost:20450';
const OUT = '/home/shan/evidence-staging/p04-shell/r8';
const PW = 'Demo-Pass-2026';
const res = {};
const shot = async (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 60 });
async function signIn(page, email) {
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]').waitFor();
  await page.fill('input[name="email"]', email);
  await page.keyboard.press('Tab');
  await page.keyboard.type(PW);
  await page.keyboard.press('Enter');
  await page.locator('nav.navpane a').first().waitFor({ timeout: 30000 });
  await page.waitForTimeout(500);
}
async function signOut(page) {
  await page.evaluate(() => fetch('/api/auth/sign-out', { method: 'POST', headers: { 'X-Erp-Request': '1' } }));
}
async function palette(page, query, { enter = false } = {}) {
  await page.keyboard.press('Control+k');
  await page.locator('[role=dialog] input[role=combobox]').waitFor();
  if (query) await page.keyboard.type(query, { delay: 20 });
  await page.waitForTimeout(700);
  const options = await page.evaluate(() => [...document.querySelectorAll('[role=dialog] [role=option]')].map(o => o.textContent.trim().replace(/\s+/g, ' ')));
  const groups = await page.evaluate(() => [...document.querySelectorAll('[role=dialog] [role=group], [role=dialog] [role=presentation]')].map(g => (g.getAttribute('aria-label') || g.textContent).trim().slice(0, 40)));
  if (enter) { await page.keyboard.press('Enter'); await page.waitForTimeout(1200); }
  else { await page.keyboard.press('Escape'); await page.waitForTimeout(200); }
  return { options, groups };
}
const state = (page) => page.evaluate(() => ({
  url: location.pathname + location.search, dir: document.documentElement.dir, lang: document.documentElement.lang, title: document.title,
  h1: document.querySelector('main h1')?.textContent?.trim(),
  focus: document.activeElement && (document.activeElement.getAttribute('name') || document.activeElement.getAttribute('aria-label') || document.activeElement.tagName + ':' + document.activeElement.textContent.trim().slice(0, 40)),
  hscroll: document.documentElement.scrollWidth > document.documentElement.clientWidth,
}));
(async () => {
  const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
  const errors = [];
  // 1. Arabic browser, first screen
  {
    const ctx = await browser.newContext({ locale: 'ar-AE', viewport: { width: 1366, height: 800 } });
    const page = await ctx.newPage();
    page.on('console', m => m.type() === 'error' && errors.push('ar-first: ' + m.text()));
    await page.goto(BASE + '/');
    await page.locator('input[name="email"]').waitFor();
    res.firstScreenAr = await state(page);
    await shot(page, '01-sign-in-ar');
    await ctx.close();
  }
  // 2. admin English: palette actions
  const ctx = await browser.newContext({ locale: 'en-US', viewport: { width: 1366, height: 800 } });
  const page = await ctx.newPage();
  page.on('console', m => m.type() === 'error' && errors.push('admin: ' + m.text()));
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  await signIn(page, 'admin@alnoor.example');
  res.home = await state(page);
  await shot(page, '02-home-en');
  res.palette = {};
  for (const q of ['', 'new user', 'new role', 'new company', 'new branch', 'invite', 'export', 'print', 'users', 'Read-only', 'ALN-DXB', 'Hessa', 'copy role', 'unblock', 'sign-in history', 'arabic', 'xyzzy']) {
    res.palette[q || '(empty)'] = await palette(page, q);
  }
  // run "new user" from home
  await palette(page, 'new user', { enter: true });
  await page.waitForTimeout(800);
  res.newUserFromPalette = await state(page);
  await shot(page, '03-new-user-from-palette');
  await page.keyboard.press('Escape'); await page.waitForTimeout(400);
  // run "new branch" from home
  await page.keyboard.press('Alt+h'); await page.waitForTimeout(600);
  await palette(page, 'new branch', { enter: true });
  await page.waitForTimeout(800);
  res.newBranchFromPalette = await state(page);
  await page.keyboard.press('Escape'); await page.waitForTimeout(400);
  // On users screen, empty palette groups
  await page.goto(BASE + '/identity/users'); await page.locator('main table tbody tr').first().waitFor();
  res.paletteOnUsers = await palette(page, '');
  await page.keyboard.press('Control+k'); await page.waitForTimeout(500); await shot(page, '04-palette-on-users-en'); await page.keyboard.press('Escape');
  // shortcut help
  await page.keyboard.press('Control+/'); await page.waitForTimeout(500);
  res.helpEn = await page.evaluate(() => document.querySelector('[role=dialog]')?.innerText.slice(0, 3000));
  await page.keyboard.press('Escape'); await page.waitForTimeout(300);
  res.afterHelpFocus = await state(page);
  // Tab order of top bar from body
  await page.keyboard.press('Alt+h'); await page.waitForTimeout(600);
  await page.evaluate(() => document.activeElement && document.activeElement.blur());
  const tabs = [];
  for (let i = 0; i < 14; i++) {
    await page.keyboard.press('Tab');
    tabs.push(await page.evaluate(() => { const a = document.activeElement; const cs = getComputedStyle(a); return `${a.tagName}[${(a.getAttribute('aria-label') || a.textContent || '').trim().slice(0, 30)}] outline=${cs.outlineStyle} ${cs.outlineWidth} ${cs.outlineColor}`; }));
  }
  res.tabOrderEn = tabs;
  // 3. Alt+L to Arabic
  await page.goto(BASE + '/identity/users'); await page.locator('main table tbody tr').first().waitFor();
  const t0 = Date.now();
  await page.keyboard.press('Alt+l');
  await page.waitForFunction(() => document.documentElement.dir === 'rtl');
  res.altLms = Date.now() - t0;
  await page.waitForTimeout(800);
  res.usersAr = await state(page);
  res.usersArLayout = await page.evaluate(() => {
    const nav = document.querySelector('nav.navpane').getBoundingClientRect();
    const main = document.querySelector('main').getBoundingClientRect();
    const th = [...document.querySelectorAll('main table thead th')].map(t => t.textContent.trim()).filter(Boolean);
    const firstRowCells = [...(document.querySelector('main table tbody tr')?.querySelectorAll('td') || [])].map(t => t.textContent.trim());
    const font = getComputedStyle(document.body).fontFamily;
    return { navRight: nav.left > main.left, th, firstRowCells, font };
  });
  await shot(page, '05-users-ar');
  res.palette.ar = {};
  for (const q of ['', 'مستخدم جديد', 'شركة جديدة', 'فرع جديد', 'طباعة', 'تصدير', 'الأدوار', 'new user']) res.palette.ar[q || '(empty)'] = await palette(page, q);
  await page.keyboard.press('Control+k'); await page.keyboard.type('مستخدم جديد'); await page.waitForTimeout(700); await shot(page, '06-palette-ar-new-user'); await page.keyboard.press('Escape');
  await page.keyboard.press('Control+/'); await page.waitForTimeout(500);
  res.helpAr = await page.evaluate(() => document.querySelector('[role=dialog]')?.innerText.slice(0, 2000));
  await shot(page, '07-shortcuts-ar');
  await page.keyboard.press('Escape');
  // print layout in Arabic
  await page.emulateMedia({ media: 'print' }); await page.waitForTimeout(400);
  await shot(page, '08-print-media-users-ar');
  res.printAr = await page.evaluate(() => ({ text: document.body.innerText.slice(0, 600), dir: document.documentElement.dir }));
  await page.emulateMedia({ media: 'screen' });
  // reload persistence
  await page.reload(); await page.locator('main table tbody tr').first().waitFor(); await page.waitForTimeout(500);
  res.afterReloadAr = await state(page);
  // 390px Arabic
  await page.setViewportSize({ width: 390, height: 780 }); await page.waitForTimeout(600);
  res.narrowAr = await state(page);
  res.narrowOverflow = await page.evaluate(() => [...document.querySelectorAll('button, a')].filter(b => b.offsetParent && b.scrollWidth > b.clientWidth + 1).map(b => b.textContent.trim().slice(0, 30)).slice(0, 10));
  await shot(page, '09-users-ar-390');
  await page.setViewportSize({ width: 1366, height: 800 }); await page.waitForTimeout(600);
  res.wideAgainNavVisible = await page.evaluate(() => { const n = document.querySelector('nav.navpane'); return !!n && n.getBoundingClientRect().width > 0 && getComputedStyle(n).visibility !== 'hidden'; });
  // back to English for the admin
  await page.keyboard.press('Alt+l'); await page.waitForTimeout(600);
  await signOut(page);
  await ctx.close();
  // 4. viewer: palette must not offer creation
  {
    const c = await browser.newContext({ viewport: { width: 1366, height: 800 } });
    const p = await c.newPage();
    await signIn(p, 'viewer@alnoor.example');
    res.viewer = {};
    for (const q of ['', 'new user', 'new company', 'new role', 'print']) res.viewer[q || '(empty)'] = await palette(p, q);
    await p.goto(BASE + '/identity/users'); await p.waitForTimeout(1500);
    res.viewer.onUsers = await palette(p, '');
    res.viewer.altN = await (async () => { await p.keyboard.press('Alt+n'); await p.waitForTimeout(800); return state(p); })();
    await signOut(p); await c.close();
  }
  // 5. no-access user
  {
    const c = await browser.newContext({ viewport: { width: 1366, height: 800 } });
    const p = await c.newPage();
    await p.goto(BASE + '/'); await p.locator('input[name="email"]').waitFor();
    await p.fill('input[name="email"]', 'noaccess@alnoor.example'); await p.keyboard.press('Tab'); await p.keyboard.type(PW); await p.keyboard.press('Enter');
    await p.waitForTimeout(3000);
    res.noaccess = await state(p);
    res.noaccessText = await p.evaluate(() => document.body.innerText.slice(0, 600));
    res.noaccessPalette = await palette(p, '');
    await shot(p, '10-noaccess');
    await signOut(p); await c.close();
  }
  // 6. Arabic admin, digits and dates
  {
    const c = await browser.newContext({ viewport: { width: 1366, height: 800 } });
    const p = await c.newPage();
    await signIn(p, 'admin.ar@alnoor.example');
    res.arAdminHome = await state(p);
    await p.goto(BASE + '/identity/users'); await p.locator('main table tbody tr').first().waitFor(); await p.waitForTimeout(800);
    res.arAdminUsers = await p.evaluate(() => ({ status: document.querySelector('footer, [role=status], .statusline')?.textContent?.trim().slice(0, 200), cells: [...document.querySelectorAll('main table tbody tr')].slice(0, 2).map(r => r.innerText.replace(/\s+/g, ' ')), breadcrumbs: document.querySelector('nav[aria-label] ol, .breadcrumbs')?.innerText }));
    await shot(p, '11-users-ar-admin');
    await signOut(p); await c.close();
  }
  res.errors = errors;
  await browser.close();
  require('fs').writeFileSync(`${OUT}/explore.json`, JSON.stringify(res, null, 1));
  console.log('done');
})().catch(e => { console.error(e); require('fs').writeFileSync(`${OUT}/explore.json`, JSON.stringify({ ...res, fatal: String(e) }, null, 1)); process.exit(1); });
