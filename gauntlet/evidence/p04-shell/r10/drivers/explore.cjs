// Critic p04 r10: drive the demo as a user, in English and Arabic, by keyboard. Writes explore.json and screenshots.
const { chromium } = require('/home/shan/critic/p04-shell-r10-plant/gauntlet/compare/node_modules/playwright-core');
const fs = require('fs');
const BASE = process.env.BASE || 'http://localhost:20450';
const OUT = '/home/shan/evidence-staging/p04-shell/r10';
const exe = require('os').homedir() + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
const res = {};
const shot = async (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 60 });
const state = async (page) => page.evaluate(() => ({
  url: location.pathname + location.search, dir: document.documentElement.dir, lang: document.documentElement.lang,
  title: document.title, focus: document.activeElement ? (document.activeElement.getAttribute('aria-label') || document.activeElement.id || document.activeElement.tagName + '.' + document.activeElement.className) : null,
  font: getComputedStyle(document.body).fontFamily,
}));
async function signIn(page, email) {
  await page.goto(BASE + '/');
  await page.waitForSelector('input[type=email], input[autocomplete=username], input[name=email]');
  const f = await state(page); res['signin-focus-' + email] = f;
  await page.keyboard.type(email); await page.keyboard.press('Enter');
  await page.waitForTimeout(400);
  await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.waitForTimeout(2500);
}
(async () => {
  const browser = await chromium.launch({ executablePath: exe });
  const errors = [];
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US', timezoneId: 'Asia/Dubai' });
  const page = await ctx.newPage();
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', (e) => errors.push(String(e)));
  await signIn(page, 'admin@alnoor.example');
  res.home = await state(page);
  res.landmarks = await page.evaluate(() => ({
    header: !!document.querySelector('header, [role=banner]'), nav: [...document.querySelectorAll('nav, [role=navigation]')].map(n => n.getAttribute('aria-label')),
    main: !!document.querySelector('main, [role=main]'), status: [...document.querySelectorAll('[role=status], footer, .status-line, .statusbar')].map(n => n.className + ':' + n.textContent.trim().slice(0, 80)),
    breadcrumbs: [...document.querySelectorAll('nav[aria-label*=read i], .breadcrumbs')].map(n => n.textContent.trim()),
  }));
  await shot(page, '01-home-en');
  // Shortcut help sheet
  await page.keyboard.press('Shift+?'); await page.waitForTimeout(500);
  res.helpEn = await page.evaluate(() => (document.querySelector('[role=dialog]') || {}).innerText || null);
  await shot(page, '02-shortcuts-en');
  await page.keyboard.press('Escape'); await page.waitForTimeout(300);
  res.afterHelpFocus = await state(page);
  // Palette: open a screen
  await page.keyboard.press('Control+k'); await page.waitForTimeout(300);
  await page.keyboard.type('roles'); await page.waitForTimeout(800);
  res.paletteRoles = await page.evaluate(() => [...document.querySelectorAll('[role=option]')].slice(0, 8).map(o => o.textContent.trim()));
  await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
  res.afterPaletteRoles = await state(page);
  // Palette: a record by name
  await page.keyboard.press('Control+k'); await page.waitForTimeout(300);
  await page.keyboard.type('hessa'); await page.waitForTimeout(1500);
  res.paletteHessa = await page.evaluate(() => [...document.querySelectorAll('[role=option]')].slice(0, 8).map(o => o.textContent.trim()));
  await page.keyboard.press('Escape');
  // Palette: an action
  await page.keyboard.press('Control+k'); await page.waitForTimeout(300);
  await page.keyboard.type('new user'); await page.waitForTimeout(800);
  res.paletteNewUser = await page.evaluate(() => [...document.querySelectorAll('[role=option]')].slice(0, 8).map(o => o.textContent.trim()));
  await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
  res.afterNewUser = await state(page);
  await page.keyboard.press('Escape'); await page.waitForTimeout(500);
  // go-to keys
  await page.keyboard.press('Escape');
  await page.locator('main').first().focus().catch(() => {});
  await page.keyboard.press('g'); await page.waitForTimeout(150); await page.keyboard.press('u'); await page.waitForTimeout(1500);
  res.goU = await state(page);
  await shot(page, '03-users-en');
  // Switch to Arabic in one step
  const t0 = Date.now();
  await page.keyboard.press('Alt+l');
  await page.waitForFunction(() => document.documentElement.dir === 'rtl', null, { timeout: 5000 }).catch(() => {});
  res.altLms = Date.now() - t0;
  await page.waitForTimeout(1200);
  res.usersAr = await state(page);
  res.navSide = await page.evaluate(() => { const n = document.querySelector('nav'); const r = n && n.getBoundingClientRect(); return r ? { left: r.left, right: r.right, width: innerWidth } : null; });
  await shot(page, '04-users-ar');
  // Latin text visible in Arabic UI (possible hard-coded strings), excluding emails and names
  res.latinInAr = await page.evaluate(() => {
    const out = new Set(); const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    let n; while ((n = w.nextNode())) { const s = n.textContent.trim(); if (!s) continue; const el = n.parentElement; if (!el || el.closest('td, [role=gridcell], [role=cell]')) continue; if (/[A-Za-z]{3,}/.test(s) && !/@/.test(s)) out.add(s.slice(0, 60)); }
    const attrs = [...document.querySelectorAll('[aria-label],[title],[placeholder]')].map(e => e.getAttribute('aria-label') || e.getAttribute('title') || e.getAttribute('placeholder')).filter(s => /[A-Za-z]{3,}/.test(s) && !/@/.test(s));
    return { text: [...out].slice(0, 40), attrs: [...new Set(attrs)].slice(0, 40) };
  });
  // Reload persists
  await page.reload(); await page.waitForTimeout(2500);
  res.afterReloadAr = await state(page);
  // Palette in Arabic: Arabic word for users
  await page.keyboard.press('Control+k'); await page.waitForTimeout(300);
  await page.keyboard.type('الأدوار'); await page.waitForTimeout(800);
  res.paletteArRoles = await page.evaluate(() => [...document.querySelectorAll('[role=option]')].slice(0, 6).map(o => o.textContent.trim()));
  await shot(page, '05-palette-ar');
  await page.keyboard.press('Escape');
  // Help sheet in Arabic
  await page.keyboard.press('Shift+?'); await page.waitForTimeout(500);
  res.helpAr = await page.evaluate(() => (document.querySelector('[role=dialog]') || {}).innerText || null);
  await shot(page, '06-shortcuts-ar');
  await page.keyboard.press('Escape');
  // Tab order focus ring check
  const rings = [];
  for (let i = 0; i < 12; i++) { await page.keyboard.press('Tab'); rings.push(await page.evaluate(() => { const e = document.activeElement; const cs = getComputedStyle(e); return { el: (e.getAttribute('aria-label') || e.textContent || e.tagName).trim().slice(0, 30), outline: cs.outlineStyle + ' ' + cs.outlineWidth, shadow: cs.boxShadow !== 'none' }; })); }
  res.focusRings = rings;
  // Print layout in Arabic
  await page.emulateMedia({ media: 'print' }); await page.waitForTimeout(500);
  await shot(page, '07-print-users-ar'); await page.emulateMedia({ media: 'screen' });
  // Narrow phone width in Arabic
  await page.setViewportSize({ width: 390, height: 800 }); await page.waitForTimeout(800);
  res.narrow = await page.evaluate(() => ({ scrollW: document.documentElement.scrollWidth, w: innerWidth }));
  await shot(page, '08-users-ar-390');
  await page.setViewportSize({ width: 1440, height: 900 }); await page.waitForTimeout(500);
  // Back to English
  await page.keyboard.press('Alt+l'); await page.waitForTimeout(1000);
  res.backEn = await state(page);
  res.errors = errors;
  await ctx.close();
  // Viewer and no-access users
  for (const who of ['viewer@alnoor.example', 'noaccess@alnoor.example']) {
    const c = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US' }); const p = await c.newPage();
    await signIn(p, who);
    await p.keyboard.press('Control+k'); await p.waitForTimeout(400);
    res['paletteEmpty-' + who] = await p.evaluate(() => [...document.querySelectorAll('[role=option]')].map(o => o.textContent.trim()));
    await p.keyboard.type('new'); await p.waitForTimeout(800);
    res['paletteNew-' + who] = await p.evaluate(() => [...document.querySelectorAll('[role=option]')].map(o => o.textContent.trim()));
    await p.keyboard.press('Escape');
    await p.goto(BASE + '/identity/users'); await p.waitForTimeout(1500);
    res['deepLinkUsers-' + who] = await p.evaluate(() => document.querySelector('main') ? document.querySelector('main').innerText.slice(0, 200) : null);
    if (who.startsWith('noaccess')) await shot(p, '09-noaccess-users-deeplink');
    await c.close();
  }
  // Arabic browser locale first screen
  const c2 = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'ar-AE' }); const p2 = await c2.newPage();
  await p2.goto(BASE + '/'); await p2.waitForTimeout(1500);
  res.signInArLocale = await state(p2);
  await shot(p2, '10-sign-in-ar');
  await c2.close();
  await browser.close();
  fs.writeFileSync(`${OUT}/explore.json`, JSON.stringify(res, null, 2));
  console.log(JSON.stringify(res, null, 2));
})().catch((e) => { console.error(e); fs.writeFileSync(`${OUT}/explore.json`, JSON.stringify(res, null, 2)); process.exit(1); });
