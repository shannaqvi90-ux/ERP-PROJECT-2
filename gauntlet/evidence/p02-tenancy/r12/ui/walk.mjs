import { createRequire } from 'module';
const require = createRequire('/home/shan/critic/p02-tenancy-r12/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
const BASE = 'http://localhost:20250';
const exe = process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
const browser = await chromium.launch({ executablePath: exe });
const log = (...a) => console.log(...a.map(x => typeof x === 'string' ? x : JSON.stringify(x)));
async function signIn(email, lang) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: lang === 'ar' ? 'ar-AE' : 'en-GB' });
  const page = await ctx.newPage();
  page.on('pageerror', e => log('PAGEERROR', e.message));
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab');
  await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.locator('nav[aria-label]').first().waitFor();
  await page.locator('main h1').waitFor();
  return { ctx, page };
}
const shot = (page, n) => page.screenshot({ path: n, type: 'jpeg', quality: 70 });
const focused = page => page.evaluate(() => { const e = document.activeElement; return e ? (e.tagName + ' ' + (e.getAttribute('name') || e.getAttribute('aria-label') || e.textContent?.slice(0, 40) || '')) : null; });
const step = process.argv[2];
if (step === 'en') {
  const { page } = await signIn('admin@alnoor.example', 'en');
  log('dir', await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang));
  // keyboard: G then C -> companies
  await page.keyboard.press('g'); await page.keyboard.press('c');
  await page.waitForURL(/tenancy\/companies/); await page.locator('table[role=grid] tbody tr').first().waitFor();
  log('companies rows', await page.locator('table[role=grid] tbody tr').count());
  await page.keyboard.press('Alt+n');
  await page.locator('.record-form').waitFor();
  log('focus after Alt+N', await focused(page));
  const name = 'Critic Twelve Trading LLC';
  await page.keyboard.type(name);
  // fill other fields by Tab and observe order
  const order = [];
  for (let i = 0; i < 12; i++) { await page.keyboard.press('Tab'); order.push(await focused(page)); }
  log('tab order', order);
  // fill Arabic name + TRN via locators
  await page.locator('[data-field="legalNameAr"] input').fill('شركة الناقد الثانية عشرة للتجارة ذ.م.م');
  const trn = page.locator('[data-field="taxRegistrationNumber"] input');
  if (await trn.count()) { await trn.fill('100-2345 6789-0003'); }
  const tl = page.locator('[data-field="tradeLicenceNumber"] input'); if (await tl.count()) await tl.fill('CN-9988776');
  log('fields', await page.locator('.record-form [data-field]').evaluateAll(els => els.map(e => e.getAttribute('data-field'))));
  await page.keyboard.press('Control+s');
  await page.locator('input[name="branchNameEn"]:focus').waitFor({ timeout: 10000 }).catch(e => log('no branch focus', e.message));
  log('after save focus', await focused(page), 'branch line value', await page.locator('input[name="branchNameEn"]').inputValue().catch(() => null));
  await page.keyboard.type('Musaffah warehouse');
  await shot(page, '../shot-01-en-new-company-branch-line.jpg');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1500);
  log('branches table', await page.locator('.record-section table tbody tr').allTextContents());
  log('form text', (await page.locator('.record-form').innerText()).slice(0, 1500));
  await shot(page, '../shot-02-en-company-saved.jpg');
  // switcher Alt+C
  await page.keyboard.press('Escape');
  await page.keyboard.press('Alt+c');
  await page.waitForTimeout(500);
  log('focus after Alt+C', await focused(page));
  await page.keyboard.type('Critic Twelve');
  await page.waitForTimeout(400);
  await shot(page, '../shot-03-en-switcher.jpg');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1500);
  log('topbar', (await page.locator('header').first().innerText()).slice(0, 300));
}
if (step === 'role') {
  const { page } = await signIn('accountant@alnoor.example', 'en');
  log('topbar', (await page.locator('header').first().innerText()).slice(0, 300));
  await page.goto(BASE + '/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  log('rows', await page.locator('table[role=grid] tbody tr').allInnerTexts());
  await page.locator('table[role=grid] tbody tr', { hasText: 'ALN-FZE' }).click();
  await page.locator('.record-form').waitFor(); await page.waitForTimeout(800);
  log('FZE form text', (await page.locator('main').innerText()).slice(0, 800));
  log('save buttons', await page.locator('.record-form button[type=submit]').count(), 'disabled fieldset', await page.locator('.record-form fieldset[disabled]').count());
  await shot(page, '../shot-04-en-role-elsewhere-readonly.jpg');
  await page.locator('table[role=grid] tbody tr', { hasText: 'ALN-DXB' }).click(); await page.waitForTimeout(800);
  log('DXB save buttons', await page.locator('.record-form button[type=submit]').count(), 'disabled fieldset', await page.locator('.record-form fieldset[disabled]').count());
  await page.goto(BASE + '/tenancy/branches'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  log('branch rows', await page.locator('table[role=grid] tbody tr').allInnerTexts());
  log('New branch button', await page.locator('main button.button.primary').allInnerTexts());
}
if (step === 'ar') {
  const { page } = await signIn('admin.ar@alnoor.example', 'ar');
  log('dir', await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang));
  await page.keyboard.press('g'); await page.keyboard.press('c');
  await page.waitForURL(/tenancy\/companies/); await page.locator('table[role=grid] tbody tr').first().waitFor();
  await shot(page, '../shot-05-ar-companies-list.jpg');
  await page.keyboard.press('Alt+n'); await page.locator('.record-form').waitFor();
  log('focus after Alt+N (ar)', await focused(page));
  await page.keyboard.type('شركة الواحة للصناعات ذ.م.م');
  await page.keyboard.press('Tab'); log('tab1', await focused(page));
  await page.keyboard.type('Al Waha Industries LLC');
  const trn = page.locator('[data-field="taxRegistrationNumber"] input'); if (await trn.count()) await trn.fill('١٠٠٣٤٥٦٧٨٩٠٠٠٠٣');
  await page.keyboard.press('Control+s');
  await page.locator('input[name="branchNameEn"]:focus').waitFor({ timeout: 10000 }).catch(e => log('no branch focus', e.message));
  log('after save focus', await focused(page));
  log('branch line ar value', await page.locator('input[name="branchNameAr"]').inputValue().catch(() => null), 'en', await page.locator('input[name="branchNameEn"]').inputValue().catch(() => null));
  await page.keyboard.type('Musaffah plant');
  await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
  log('form text', (await page.locator('.record-form').innerText()).slice(0, 1200));
  await shot(page, '../shot-06-ar-company-saved.jpg');
  await page.keyboard.press('Escape');
  await page.keyboard.press('Alt+c'); await page.waitForTimeout(500);
  log('focus after Alt+C (ar)', await focused(page));
  await page.keyboard.type('الواحة'); await page.waitForTimeout(400);
  await shot(page, '../shot-07-ar-switcher.jpg');
  await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
  log('topbar ar', (await page.locator('header').first().innerText()).slice(0, 300));
  // company profile print (Arabic)
}
await browser.close();
