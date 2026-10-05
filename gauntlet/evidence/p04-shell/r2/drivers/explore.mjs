// Critic's exploration of the app shell in a real Chromium, English and Arabic, keyboard first.
// Usage: node explore.mjs <baseUrl> <outDir>
import { createRequire } from 'node:module';
import fs from 'node:fs';
const require = createRequire(process.env.PW_CORE_FROM || '/home/user/critic/p04-shell-r2/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');

const base = process.argv[2] || 'http://localhost:20450';
const out = process.argv[3] || '/home/user/evidence-staging/p04-shell/r2';
const PASS = 'Demo-Pass-2026';
const log = [];
const note = (k, v) => { log.push({ k, v }); console.log(k, typeof v === 'string' ? v : JSON.stringify(v)); };
const shot = async (page, name) => page.screenshot({ path: `${out}/${name}.jpg`, type: 'jpeg', quality: 60 });

const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });

async function signIn(page, email) {
  await page.goto(base + '/');
  const emailBox = page.locator('input[name="email"]');
  await emailBox.waitFor();
  await emailBox.fill(email);
  await page.locator('input[name="password"]').fill(PASS);
  await page.keyboard.press('Enter');
  await page.locator('nav.navpane').waitFor({ timeout: 30000 });
}

async function main() {
  // 1. First screen in an Arabic browser: RTL before sign-in.
  {
    const ctx = await browser.newContext({ locale: 'ar-AE', viewport: { width: 1280, height: 800 } });
    const page = await ctx.newPage();
    await page.goto(base + '/');
    await page.locator('input[name="email"]').waitFor();
    note('signin.ar-AE', await page.evaluate(() => ({ dir: document.documentElement.dir, lang: document.documentElement.lang, h1: document.querySelector('h1')?.textContent, focused: document.activeElement?.getAttribute('name') })));
    await ctx.close();
  }

  const ctx = await browser.newContext({ locale: 'en-US', viewport: { width: 1366, height: 820 } });
  const page = await ctx.newPage();
  const consoleErrors = [];
  page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
  await signIn(page, 'admin@alnoor.example');
  await page.waitForTimeout(500);
  note('home.en', await page.evaluate(() => ({
    dir: document.documentElement.dir, lang: document.documentElement.lang,
    topbar: !!document.querySelector('header.topbar'), nav: [...document.querySelectorAll('nav.navpane a')].map(a => a.textContent.trim() + ' ' + a.getAttribute('href')),
    status: document.querySelector('footer.statusbar')?.textContent, focus: document.activeElement?.outerHTML.slice(0, 120),
  })));
  await shot(page, '01-home-en');

  // 2. Navigate by keyboard: Alt+M then arrows.
  await page.keyboard.press('Alt+m');
  await page.waitForTimeout(200);
  note('altM.focus', await page.evaluate(() => document.activeElement?.textContent?.trim() + ' | ' + document.activeElement?.getAttribute('href')));
  await page.keyboard.press('ArrowDown');
  note('altM.down', await page.evaluate(() => document.activeElement?.textContent?.trim() + ' | ' + document.activeElement?.getAttribute('href')));
  await page.keyboard.press('Enter');
  await page.waitForTimeout(800);
  note('altM.enter.url', page.url());
  note('breadcrumbs', await page.evaluate(() => document.querySelector('nav[aria-label] ol, .breadcrumbs')?.textContent));
  note('after-nav.focus', await page.evaluate(() => document.activeElement?.tagName + ' ' + (document.activeElement?.className || '') + ' ' + (document.activeElement?.getAttribute('aria-label') || '')));

  // 3. Palette: open users screen, search a record, show all.
  await page.keyboard.press('Control+k');
  await page.locator('[role=dialog] input[role=combobox]').waitFor();
  await page.keyboard.type('users');
  await page.waitForTimeout(800);
  note('palette.users', await page.evaluate(() => [...document.querySelectorAll('[role=option]')].map(o => o.textContent.trim()).slice(0, 12)));
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1200);
  note('palette.enter.url', page.url());
  note('users.rows', await page.evaluate(() => document.querySelectorAll('main table tbody tr, main [role=row]').length));
  await shot(page, '02-users-en');

  // 4. Tab order on the users screen: what gets focus, is a focus ring visible?
  const tabs = [];
  await page.evaluate(() => document.querySelector('main')?.focus());
  for (let i = 0; i < 14; i++) {
    await page.keyboard.press('Tab');
    tabs.push(await page.evaluate(() => {
      const e = document.activeElement; const cs = e ? getComputedStyle(e) : null;
      return `${e?.tagName}.${(e?.className || '').toString().slice(0, 30)} [${(e?.getAttribute('aria-label') || e?.textContent || '').trim().slice(0, 30)}] outline=${cs?.outlineStyle}/${cs?.outlineWidth} shadow=${cs?.boxShadow !== 'none'}`;
    }));
  }
  note('users.tab-order', tabs);

  // 5. Open a record by keyboard from the list.
  const rowFocus = await page.evaluate(() => {
    const r = document.querySelector('main [role=row][tabindex], main tbody tr[tabindex], main [role=grid]');
    return r ? r.outerHTML.slice(0, 200) : null;
  });
  note('users.focusable-row', rowFocus);

  // 6. Shortcut help sheet.
  await page.evaluate(() => document.querySelector('main')?.focus());
  await page.keyboard.press('Shift+Slash');
  await page.waitForTimeout(400);
  note('help.sheet', await page.evaluate(() => [...document.querySelectorAll('[role=dialog] tr, [role=dialog] li, [role=dialog] dt')].map(x => x.textContent.trim().replace(/\s+/g, ' ')).slice(0, 40)));
  await shot(page, '03-shortcuts-en');
  await page.keyboard.press('Escape');
  await page.waitForTimeout(200);
  note('help.esc.focus', await page.evaluate(() => document.activeElement?.tagName + ' ' + document.activeElement?.className));

  // 7. Switch to Arabic in one step (Alt+L), time to RTL.
  const t0 = Date.now();
  await page.keyboard.press('Alt+l');
  await page.waitForFunction(() => document.documentElement.dir === 'rtl');
  note('alt+L.ms', Date.now() - t0);
  await page.waitForTimeout(700);
  const ar = await page.evaluate(() => {
    const latin = [];
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    while (walker.nextNode()) {
      const n = walker.currentNode; const t = n.textContent.trim();
      if (!t || !/[A-Za-z]{3,}/.test(t)) continue;
      const p = n.parentElement; if (p.closest('table tbody, [dir=ltr], .ltr, kbd, .keys, script, style')) continue;
      latin.push(`${p.tagName}.${p.className}: ${t.slice(0, 40)}`);
    }
    const nav = document.querySelector('nav.navpane'); const main = document.querySelector('main');
    const navRect = nav.getBoundingClientRect(); const mainRect = main.getBoundingClientRect();
    const th = [...document.querySelectorAll('main thead th, main [role=columnheader]')].map(t => ({ text: t.textContent.trim(), x: Math.round(t.getBoundingClientRect().x), align: getComputedStyle(t).textAlign }));
    return {
      dir: document.documentElement.dir, lang: document.documentElement.lang,
      font: getComputedStyle(document.body).fontFamily,
      navOnRight: navRect.x > mainRect.x,
      latinTextOutsideData: latin.slice(0, 30),
      headers: th.slice(0, 8),
      h1: document.querySelector('main h1')?.textContent,
      status: document.querySelector('footer.statusbar')?.textContent,
      count: document.querySelector('main')?.textContent.match(/[\d٠-٩][\d٠-٩,٬.]*\s*\S+/)?.[0],
    };
  });
  note('arabic.state', ar);
  await shot(page, '04-users-ar');
  // Rendered font for Arabic text
  const cdp = await ctx.newCDPSession(page);
  await cdp.send('DOM.enable'); await cdp.send('CSS.enable');
  const doc = await cdp.send('DOM.getDocument');
  const h1 = await cdp.send('DOM.querySelector', { nodeId: doc.root.nodeId, selector: 'main h1' });
  if (h1.nodeId) note('arabic.platformFonts', (await cdp.send('CSS.getPlatformFontsForNode', { nodeId: h1.nodeId })).fonts);

  // 8. Palette in Arabic: type Arabic name of a screen.
  await page.keyboard.press('Control+k');
  await page.locator('[role=dialog] input[role=combobox]').waitFor();
  await page.keyboard.type('المستخدم');
  await page.waitForTimeout(900);
  note('palette.ar', await page.evaluate(() => [...document.querySelectorAll('[role=option]')].map(o => o.textContent.trim()).slice(0, 10)));
  await shot(page, '05-palette-ar');
  await page.keyboard.press('Escape');

  // 9. Persisted: reload, then a new browser context.
  await page.reload();
  await page.locator('nav.navpane').waitFor();
  note('reload.dir', await page.evaluate(() => document.documentElement.dir));
  const ctx2 = await browser.newContext({ locale: 'en-US' });
  const p2 = await ctx2.newPage();
  await signIn(p2, 'admin@alnoor.example');
  note('newdevice.dir', await p2.evaluate(() => document.documentElement.dir + ' ' + document.documentElement.lang));
  await ctx2.close();

  // 10. Print layout in Arabic (users list).
  await page.emulateMedia({ media: 'print' });
  await page.waitForTimeout(300);
  await shot(page, '06-print-ar');
  note('print.ar', await page.evaluate(() => {
    const vis = e => e && getComputedStyle(e).display !== 'none' && getComputedStyle(e).visibility !== 'hidden';
    return { navVisible: vis(document.querySelector('nav.navpane')), topbarVisible: vis(document.querySelector('header.topbar')), letterhead: document.querySelector('.print-header, [data-print-header], .print-letterhead')?.textContent?.trim().slice(0, 120) };
  }));
  await page.pdf({ path: `${out}/print-users-ar.pdf`, format: 'A4' });
  await page.emulateMedia({ media: 'screen' });

  // 11. Preferences (Alt+P) and digits.
  await page.keyboard.press('Alt+p');
  await page.waitForTimeout(400);
  note('prefs.dialog', await page.evaluate(() => document.querySelector('[role=dialog]')?.textContent.trim().replace(/\s+/g, ' ').slice(0, 300)));
  await shot(page, '07-preferences-ar');
  await page.keyboard.press('Escape');

  // 12. Narrow width.
  await page.setViewportSize({ width: 390, height: 800 });
  await page.waitForTimeout(400);
  note('narrow.scroll', await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth })));
  await shot(page, '08-narrow-ar');
  await page.setViewportSize({ width: 1366, height: 820 });

  // back to English for the demo users
  await page.keyboard.press('Alt+l');
  await page.waitForFunction(() => document.documentElement.dir === 'ltr');
  await page.waitForTimeout(500);
  note('console.errors', consoleErrors.slice(0, 10));
  await ctx.close();
}

try { await main(); } catch (e) { note('ERROR', String(e.stack || e)); }
fs.writeFileSync(`${out}/explore.json`, JSON.stringify(log, null, 1));
await browser.close();
