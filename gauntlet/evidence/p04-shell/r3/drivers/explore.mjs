// Critic's exploration of the shell in a real browser (Chromium), English and Arabic, keyboard.
// Usage: node explore.mjs <baseUrl> <outDir>   (run from a folder that has playwright-core)
import { chromium } from 'playwright-core';
import { writeFileSync } from 'node:fs';
const base = process.argv[2] ?? 'http://localhost:20450';
const out = process.argv[3] ?? '.';
const pw = 'Demo-Pass-2026';
const R = {};
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const shot = (page, name) => page.screenshot({ path: `${out}/${name}.jpg`, type: 'jpeg', quality: 60 });
async function signIn(page, email) {
  await page.goto(base + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type(pw); await page.keyboard.press('Enter');
  await page.locator('nav.navpane').waitFor();
  await page.waitForTimeout(500);
}
const frame = (page) => page.evaluate(() => {
  const s = document.querySelector('footer.statusbar, .statusbar')?.getBoundingClientRect();
  return { inner: [innerWidth, innerHeight], scroll: [document.documentElement.scrollWidth, document.documentElement.scrollHeight], status: s ? [Math.round(s.top), Math.round(s.bottom)] : null, dir: document.documentElement.dir, lang: document.documentElement.lang, title: document.title };
});
// 1. English home and users at 1366x768
{
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 }, locale: 'en-US' });
  const page = await ctx.newPage();
  const apiNoStore = [];
  page.on('response', r => { if (new URL(r.url()).pathname.startsWith('/api/')) apiNoStore.push(`${r.status()} ${new URL(r.url()).pathname} cache-control=${r.headers()['cache-control']}`); });
  await signIn(page, 'admin@alnoor.example');
  R.homeEn = await frame(page);
  await shot(page, '01-home-en');
  // keyboard: Tab order from home, focus ring
  await page.locator('main h1').first().click();
  const tabs = [];
  for (let i = 0; i < 25; i++) {
    await page.keyboard.press('Tab');
    tabs.push(await page.evaluate(() => { const e = document.activeElement; const cs = getComputedStyle(e); return `${e.tagName.toLowerCase()}${e.getAttribute('aria-label') ? '[' + e.getAttribute('aria-label') + ']' : ''} "${(e.textContent || e.getAttribute('placeholder') || '').trim().slice(0, 30)}" outline=${cs.outlineStyle} ${cs.outlineWidth} ${cs.outlineColor}`; }));
  }
  R.tabOrderHome = tabs;
  // Alt+M navigation then users list
  await page.keyboard.press('Alt+m');
  R.altM = await page.evaluate(() => document.activeElement?.getAttribute('href'));
  await page.goto(base + '/identity/users');
  await page.locator('main table tbody tr').first().waitFor();
  await page.waitForTimeout(500);
  R.usersEn = await frame(page);
  await shot(page, '02-users-en');
  // shortcut help
  await page.locator('main h1').first().click();
  await page.keyboard.press('Shift+Slash');
  await page.waitForTimeout(400);
  R.helpText = (await page.locator('[role=dialog]').innerText()).slice(0, 2500);
  await shot(page, '03-shortcut-help-en');
  await page.keyboard.press('Escape');
  R.focusAfterEsc = await page.evaluate(() => document.activeElement?.tagName + ' ' + (document.activeElement?.textContent || '').slice(0, 30));
  R.apiHeaders = [...new Set(apiNoStore)].slice(0, 40);
  R.cookies = (await ctx.cookies()).map(c => `${c.name} httpOnly=${c.httpOnly} secure=${c.secure} sameSite=${c.sameSite}`);
  // 2. Switch to Arabic in one step
  const t0 = Date.now();
  await page.keyboard.press('Alt+l');
  await page.waitForFunction(() => document.documentElement.dir === 'rtl');
  R.switchMs = Date.now() - t0;
  await page.waitForTimeout(800);
  R.usersAr = await frame(page);
  R.usersArHeaders = await page.locator('main table thead th').allInnerTexts();
  R.usersArFirstRow = await page.locator('main table tbody tr').first().innerText();
  R.navRect = await page.evaluate(() => { const n = document.querySelector('nav.navpane').getBoundingClientRect(); return [Math.round(n.left), Math.round(n.right)]; });
  R.hardcodedEnglishInAr = await page.evaluate(() => { const t = document.body.innerText; return (t.match(/\b[A-Za-z]{4,}\b/g) || []).filter(w => !/example|alnoor|admin/i.test(w)).slice(0, 40); });
  // font actually used for Arabic
  const cdp = await ctx.newCDPSession(page);
  await cdp.send('DOM.enable'); await cdp.send('CSS.enable');
  const { root } = await cdp.send('DOM.getDocument');
  const { nodeId } = await cdp.send('DOM.querySelector', { nodeId: root.nodeId, selector: 'main h1' });
  R.arabicFonts = (await cdp.send('CSS.getPlatformFontsForNode', { nodeId })).fonts;
  await shot(page, '04-users-ar');
  // palette in Arabic
  await page.keyboard.press('Control+k');
  await page.keyboard.type('مستخدم');
  await page.waitForTimeout(800);
  R.paletteAr = (await page.locator('[role=dialog]').innerText()).slice(0, 800);
  await shot(page, '05-palette-ar');
  await page.keyboard.press('Escape');
  // reload keeps Arabic
  await page.reload(); await page.locator('nav.navpane').waitFor(); await page.waitForTimeout(500);
  R.afterReload = await frame(page);
  // print emulation in Arabic
  await page.emulateMedia({ media: 'print' });
  await page.waitForTimeout(300);
  await shot(page, '06-print-ar');
  await page.pdf({ path: `${out}/print-users-ar.pdf`, format: 'A4' });
  R.printVisibleButtons = await page.evaluate(() => [...document.querySelectorAll('button, input, select')].filter(e => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0 && getComputedStyle(e).visibility !== 'hidden'; }).map(e => (e.getAttribute('aria-label') || e.textContent || e.type).trim().slice(0, 30)));
  await page.emulateMedia({ media: 'screen' });
  // a new browser follows the user (persisted per user)
  const ctx2 = await browser.newContext({ viewport: { width: 1366, height: 768 }, locale: 'en-US' });
  const p2 = await ctx2.newPage();
  await signIn(p2, 'admin@alnoor.example');
  R.otherBrowser = await frame(p2);
  await ctx2.close();
  // switch back to English
  await page.keyboard.press('Alt+l');
  await page.waitForFunction(() => document.documentElement.dir === 'ltr');
  // narrow width, Arabic
  await ctx.close();
}
// 3. Arabic user at 390px and 1280x720, numbers and dates
{
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'ar-AE' });
  const page = await ctx.newPage();
  await page.goto(base + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  R.signInAr = await frame(page);
  await shot(page, '07-sign-in-ar');
  await signIn(page, 'admin.ar@alnoor.example');
  await page.goto(base + '/identity/users');
  await page.locator('main table tbody tr').first().waitFor(); await page.waitForTimeout(600);
  R.users1280Ar = await frame(page);
  R.arTotals = await page.evaluate(() => (document.body.innerText.match(/[٠-٩0-9][٠-٩0-9,٬.]*[^\n]{0,20}/g) || []).slice(0, 15));
  await page.setViewportSize({ width: 390, height: 800 });
  await page.waitForTimeout(500);
  R.users390Ar = await frame(page);
  await shot(page, '08-users-ar-390');
  await ctx.close();
}
writeFileSync(`${out}/explore.json`, JSON.stringify(R, null, 1));
await browser.close();
console.log(JSON.stringify(R, null, 1));
