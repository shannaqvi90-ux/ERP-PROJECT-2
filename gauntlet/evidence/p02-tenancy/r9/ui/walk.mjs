// Critic p02 r9 browser walk: keyboard, English and Arabic, full and limited administrators.
// node walk.mjs <base> <clPassword> <clEmail> <blEmail>
import { createRequire } from 'node:module';
const require = createRequire('/home/shan/critic/p02-tenancy-r9/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
const [base, limitedPw, clEmail, blEmail] = process.argv.slice(2);
const out = '/home/shan/evidence-staging/p02-tenancy/r9/';
const browser = await chromium.launch({ executablePath: `${process.env.HOME}/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome` });
const log = (...a) => console.log(...a);
const tag = Date.now().toString(36).slice(-4).toUpperCase();

async function session(email, password, lang) {
  const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 } });
  const page = await ctx.newPage();
  await page.goto(base + '/');
  await page.evaluate((l) => { localStorage.clear(); localStorage.setItem('erp.language', l); }, lang);
  await page.goto(base + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type(password); await page.keyboard.press('Enter');
  await page.locator('nav[aria-label]').first().waitFor();
  await page.locator('main h1').waitFor();
  return { ctx, page };
}
const shot = (page, name) => page.screenshot({ path: out + name, type: 'jpeg', quality: 60 });
const offered = async (page) => page.evaluate(() => ({
  newButtons: [...document.querySelectorAll('main button')].filter((b) => /^(New|جديد)/.test(b.textContent.trim())).length,
  codeDisabled: document.querySelector('[data-field="code"] input')?.disabled ?? null,
  formOpen: !!document.querySelector('.record-form'),
  dir: document.documentElement.dir, lang: document.documentElement.lang,
  overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
}));

if (!process.env.LIMITED_ONLY) {
// 1. English admin, keyboard only: reach Companies, Alt+N, name, Ctrl+S, branch line, Ctrl+S.
{
  const { ctx, page } = await session('admin@alnoor.example', 'Demo-Pass-2026', 'en');
  await page.goto(base + '/tenancy/companies');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.keyboard.press('Alt+n');
  await page.locator('[data-field="legalNameEn"] input:focus').waitFor();
  await page.keyboard.type(`Critic Walk ${tag} Trading LLC`);
  await page.keyboard.press('Tab');
  await page.keyboard.type('كريتك ووك للتجارة ذ.م.م');
  await page.keyboard.press('Control+s');
  await page.locator('input[name="branchNameEn"]:focus').waitFor({ timeout: 10000 });
  log('EN branch line prefilled:', JSON.stringify(await page.locator('input[name="branchNameEn"]').inputValue()));
  await page.keyboard.type('Jebel Ali');
  // Arabic branch name: Tab to it if present
  await page.keyboard.press('Tab');
  const focusedName = await page.evaluate(() => document.activeElement?.getAttribute('name'));
  log('EN after Tab from branch name, focus on:', focusedName, 'value', JSON.stringify(await page.evaluate(() => document.activeElement?.value)));
  if (focusedName === 'branchNameAr') { await page.keyboard.press('Control+a'); await page.keyboard.type('فرع جبل علي'); }
  await page.keyboard.press('Control+s');
  await page.waitForTimeout(1500);
  const rows = await page.locator('.record-section table tbody tr').allTextContents();
  log('EN branches under company after Ctrl+S:', JSON.stringify(rows));
  log('EN state', JSON.stringify(await offered(page)));
  await shot(page, '01-company-branch-keyboard-en.jpg');
  // Switch company by keyboard: Alt+C, type, Enter
  await page.keyboard.press('Escape');
  await page.keyboard.press('Alt+c');
  await page.waitForTimeout(400);
  await page.keyboard.type('Sharjah');
  await page.waitForTimeout(300);
  await shot(page, '02-switcher-open-en.jpg');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1000);
  log('EN workplace after switch:', JSON.stringify(await page.evaluate(async () => (await (await fetch('/api/tenancy/workplace', { headers: { 'x-erp-request': '1' } })).json()))?.companyId ?? null));
  log('EN top bar text:', (await page.locator('header').first().innerText()).replace(/\s+/g, ' ').slice(0, 300));
  await ctx.close();
}
// 2. Arabic admin, keyboard
{
  const { ctx, page } = await session('admin.ar@alnoor.example', 'Demo-Pass-2026', 'ar');
  await page.goto(base + '/tenancy/companies');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  log('AR list state', JSON.stringify(await offered(page)));
  await page.keyboard.press('Alt+n');
  await page.locator('[data-field="legalNameEn"] input:focus, [data-field="legalNameAr"] input:focus').first().waitFor();
  const first = await page.evaluate(() => document.activeElement?.closest('[data-field]')?.getAttribute('data-field'));
  log('AR new company first field:', first);
  if (first === 'legalNameAr') { await page.keyboard.type('شركة الناقد للتصنيع ذ.م.م ' + tag); await page.keyboard.press('Shift+Tab'); await page.keyboard.type(`Critic Walk AR ${tag} Manufacturing LLC`); }
  else { await page.keyboard.type(`Critic Walk AR ${tag} Manufacturing LLC`); await page.keyboard.press('Tab'); await page.keyboard.type('شركة الناقد للتصنيع ذ.م.م ' + tag); }
  await page.keyboard.press('Control+s');
  await page.waitForTimeout(1500);
  const fname = await page.evaluate(() => document.activeElement?.getAttribute('name'));
  log('AR after save, focus on', fname, JSON.stringify(await page.evaluate(() => document.activeElement?.value)));
  await page.keyboard.press('Control+a'); await page.keyboard.type('فرع المصفح');
  await page.keyboard.press('Control+s');
  await page.waitForTimeout(1500);
  log('AR branches under company:', JSON.stringify(await page.locator('.record-section table tbody tr').allTextContents()));
  log('AR state', JSON.stringify(await offered(page)));
  await shot(page, '03-company-branch-keyboard-ar.jpg');
  // print the company profile in Arabic
  await ctx.close();
}
}
// 3. One-company administrator (ALN-SHJ only)
for (const [who, email] of [['CL', clEmail], ['BL', blEmail]]) {
  const { ctx, page } = await session(email, limitedPw, 'en');
  await page.goto(base + '/tenancy/companies');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.waitForTimeout(500);
  log(`${who} list`, JSON.stringify(await offered(page)));
  await page.keyboard.press('Alt+n'); await page.waitForTimeout(600);
  log(`${who} after Alt+N`, JSON.stringify(await offered(page)), new URL(page.url()).search);
  await page.goto(base + '/tenancy/companies?open=new'); await page.waitForTimeout(1500);
  log(`${who} ?open=new`, JSON.stringify(await offered(page)), new URL(page.url()).search);
  await page.goto(base + '/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.locator('.record-form').waitFor(); await page.waitForTimeout(800);
  log(`${who} record`, JSON.stringify(await offered(page)), 'hint:', JSON.stringify(await page.locator('[data-field="code"]').innerText().catch(() => '')));
  log(`${who} save buttons`, await page.locator('.record-form button[type=submit]').count(), 'disabled inputs', await page.locator('.record-form input:disabled').count(), 'enabled inputs', await page.locator('.record-form input:enabled').count());
  await shot(page, who === 'CL' ? '04-one-company-admin-record-en.jpg' : '05-one-branch-admin-record-en.jpg');
  // Palette: Alt+P / Ctrl+K search for "new company"
  for (const k of ['Control+k', 'Alt+p']) {
    await page.keyboard.press('Escape'); await page.keyboard.press(k); await page.waitForTimeout(300);
    await page.keyboard.type('new compan'); await page.waitForTimeout(400);
    const options = await page.locator('[role=option]').allTextContents();
    log(`${who} palette ${k}:`, JSON.stringify(options.slice(0, 6)));
    await page.keyboard.press('Escape');
  }
  await ctx.close();
}
await browser.close();
