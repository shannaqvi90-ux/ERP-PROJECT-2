// Critic p02 r10 walk 2: legacy TRN hint (en, ar); switching with eight active companies on a fresh
// device after one earlier switch elsewhere; no-company administrator. Usage: node walk2.mjs <base>
import { createRequire } from 'node:module';
const require = createRequire('/home/shan/critic/p02-tenancy-r10/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
const base = process.argv[2] ?? 'http://localhost:20250';
const out = '/home/shan/evidence-staging/p02-tenancy/r10/';
const browser = await chromium.launch({ executablePath: `${process.env.HOME}/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome` });
const log = (...a) => console.log(...a);
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
const api = (page, method, path, body) => page.evaluate(async ([m, p, b]) => {
  const r = await fetch(p, { method: m, headers: { 'x-erp-request': '1', ...(b ? { 'content-type': 'application/json' } : {}) }, body: b ? JSON.stringify(b) : undefined });
  return { status: r.status, json: await r.json().catch(() => null) };
}, [method, path, body]);
const fieldState = (page, name) => page.evaluate((n) => {
  const f = document.querySelector(`[data-field="${n}"]`);
  return f ? { hint: f.querySelector('.field-hint')?.textContent ?? null, error: f.querySelector('.field-error')?.textContent ?? null, value: f.querySelector('input')?.value } : null;
}, name);

// 1. Legacy TRN hint, English then Arabic; save the company with the TRN left blank; legacy kept?
for (const [email, lang] of [['admin@alnoor.example', 'en'], ['admin.ar@alnoor.example', 'ar']]) {
  const { ctx, page } = await session(email, 'Demo-Pass-2026', lang);
  const c = (await api(page, 'GET', '/api/tenancy/companies?search=CRITIC-2&take=5')).json.items.find((x) => x.code === 'CRITIC-2');
  await page.goto(base + `/tenancy/companies?open=${c.id}`);
  await page.locator('[data-field="taxRegistrationNumber"]').waitFor();
  await page.waitForTimeout(600);
  log(lang, 'legacy TRN field:', JSON.stringify(await fieldState(page, 'taxRegistrationNumber')));
  if (lang === 'en') {
    await page.locator('[data-field="legalNameEn"] input').fill('Critic R10 AR Manufacturing LLC (edited)');
    await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
    const after = (await api(page, 'GET', `/api/tenancy/companies/${c.id}`)).json;
    log('en after save with TRN blank: trn', after.taxRegistrationNumber, 'legacy', after.legacyTaxRegistrationNumber);
    await shot(page, '05-legacy-trn-hint-en.jpg');
  } else {
    await shot(page, '06-legacy-trn-hint-ar.jpg');
  }
  await ctx.close();
}

// 2. Eight active companies: add two through the API as the administrator.
{
  const { ctx, page } = await session('admin@alnoor.example', 'Demo-Pass-2026', 'en');
  const all = (await api(page, 'GET', '/api/tenancy/companies?take=200')).json.items;
  log('active companies before:', all.filter((c) => c.isActive).map((c) => c.code).join(' '));
  for (const [code, en, ar] of [['CRX-RAK', 'Critic RAK Trading LLC', 'الناقد رأس الخيمة'], ['CRX-AJM', 'Critic Ajman Industries LLC', 'الناقد عجمان'], ['CRX-UAQ', 'Critic UAQ Foods LLC', 'الناقد أم القيوين']]) {
    if (all.some((c) => c.code === code)) continue;
    const r = await api(page, 'POST', '/api/tenancy/companies', { code, legalNameEn: en, legalNameAr: ar, baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, country: 'AE', isActive: true });
    if (r.status === 201 || r.status === 200) await api(page, 'POST', '/api/tenancy/branches', { companyId: r.json.id, code: 'MAIN', nameEn: 'Main office', nameAr: 'المكتب الرئيسي', country: 'AE', isActive: true });
    log('created', code, r.status);
  }
  const wp = (await api(page, 'GET', '/api/tenancy/workplace')).json;
  log('workplace companies:', wp.companies.length, 'recent (server):', JSON.stringify(wp.recentCompanyIds));
  await ctx.close();
}
// 2a. Fresh device for viewer-like admin: before any switch.
{
  const { ctx, page } = await session('admin@alnoor.example', 'Demo-Pass-2026', 'en');
  await page.waitForTimeout(800);
  log('device 1 before switching: chips', JSON.stringify(await page.locator('.workplace-chip').allTextContents()), 'current', await page.locator('[data-testid="workplace"]').textContent());
  await shot(page, '07-top-bar-eight-companies-first-device-en.jpg');
  // switch by keyboard to SHJ then back to DXB? Use Alt+C type Sharjah Enter
  await page.keyboard.press('Alt+c'); await page.waitForTimeout(300); await page.keyboard.type('Sharjah'); await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
  log('device 1 after Alt+C Sharjah: current', await page.locator('[data-testid="workplace"]').textContent(), 'chips', JSON.stringify(await page.locator('.workplace-chip').allTextContents()));
  await ctx.close();
}
{
  const { ctx, page } = await session('admin@alnoor.example', 'Demo-Pass-2026', 'en');
  await page.waitForTimeout(800);
  const chips = await page.locator('.workplace-chip').allTextContents();
  log('device 2 (fresh storage): current', await page.locator('[data-testid="workplace"]').textContent(), 'chips', JSON.stringify(chips));
  await shot(page, '08-top-bar-eight-companies-second-device-en.jpg');
  if (chips.length) {
    await page.locator('.workplace-chip').first().click(); await page.waitForTimeout(1000);
    log('device 2 one click on first chip -> current', await page.locator('[data-testid="workplace"]').textContent());
  }
  await ctx.close();
}
{
  const { ctx, page } = await session('admin.ar@alnoor.example', 'Demo-Pass-2026', 'ar');
  await page.waitForTimeout(800);
  log('arabic admin (never switched) chips', JSON.stringify(await page.locator('.workplace-chip').allTextContents()), 'current', await page.locator('[data-testid="workplace"]').textContent(),
    'overflow', await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth));
  await ctx.close();
}
await browser.close();
