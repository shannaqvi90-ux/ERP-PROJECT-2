// Critic p02 r10 browser walk: keyboard, English and Arabic; TRN rule; Arabic-legal-name setting;
// branch on the Arabic path; legacy TRN hint. Usage: node walk.mjs <base>
import { createRequire } from 'node:module';
const require = createRequire('/home/shan/critic/p02-tenancy-r10/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
const base = process.argv[2] ?? 'http://localhost:20250';
const out = '/home/shan/evidence-staging/p02-tenancy/r10/';
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
const api = (page, method, path, body) => page.evaluate(async ([m, p, b]) => {
  const r = await fetch(p, { method: m, headers: { 'x-erp-request': '1', ...(b ? { 'content-type': 'application/json' } : {}) }, body: b ? JSON.stringify(b) : undefined });
  return { status: r.status, json: await r.json().catch(() => null) };
}, [method, path, body]);
const fieldState = (page, name) => page.evaluate((n) => {
  const f = document.querySelector(`[data-field="${n}"]`);
  return f ? { label: f.querySelector('label')?.textContent, hint: f.querySelector('.field-hint')?.textContent ?? null, error: f.querySelector('.field-error')?.textContent ?? null, required: f.querySelector('input')?.required ?? null, value: f.querySelector('input')?.type === 'checkbox' ? f.querySelector('input').checked : f.querySelector('input')?.value } : null;
}, name);
const order = (page) => page.evaluate(() => [...document.querySelectorAll('.record-form [data-field]')].slice(0, 6).map((f) => f.getAttribute('data-field')).join(' > '));

// 1. English admin, keyboard: new company with a TRN typed with dashes and spaces; bad TRN first.
{
  const { ctx, page } = await session('admin@alnoor.example', 'Demo-Pass-2026', 'en');
  await page.goto(base + '/tenancy/companies');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.keyboard.press('Alt+n');
  await page.locator('[data-field="legalNameEn"] input:focus').waitFor();
  log('EN field order:', await order(page));
  await page.keyboard.type(`Critic R10 ${tag} Trading LLC`);
  await page.keyboard.press('Tab');
  await page.keyboard.type('الناقد للتجارة ذ.م.م');
  // TRN: too short first
  await page.locator('[data-field="taxRegistrationNumber"] input').fill('12345');
  log('EN TRN hint before save:', JSON.stringify(await fieldState(page, 'taxRegistrationNumber')));
  await page.keyboard.press('Control+s');
  await page.waitForTimeout(1500);
  log('EN TRN 12345 after Ctrl+S:', JSON.stringify(await fieldState(page, 'taxRegistrationNumber')), 'focus on', await page.evaluate(() => document.activeElement?.closest('[data-field]')?.getAttribute('data-field')));
  await shot(page, '01-trn-refused-en.jpg');
  // Arabic-Indic digits (an Arabic keyboard's digits)
  await page.locator('[data-field="taxRegistrationNumber"] input').fill('١٠٠١٢٣٤٥٦٧٠٠٠٠٣');
  await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
  log('EN TRN Arabic-Indic digits after Ctrl+S:', JSON.stringify(await fieldState(page, 'taxRegistrationNumber')));
  // 16 digits
  await page.locator('[data-field="taxRegistrationNumber"] input').fill('1001234567000039');
  await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
  log('EN TRN 16 digits after Ctrl+S:', JSON.stringify(await fieldState(page, 'taxRegistrationNumber')));
  await page.locator('[data-field="taxRegistrationNumber"] input').fill('100-1234 5670-0003');
  await page.keyboard.press('Control+s');
  await page.locator('input[name="branchNameEn"]:focus').waitFor({ timeout: 10000 }).catch(() => log('EN branch line did not take focus'));
  const created = new URL(page.url()).searchParams.get('open');
  const rec = await api(page, 'GET', `/api/tenancy/companies/${created}`);
  log('EN saved company:', rec.status, 'trn', JSON.stringify(rec.json?.taxRegistrationNumber), 'arabicRequired', rec.json?.arabicLegalNameRequired);
  await page.keyboard.type(' Jebel Ali');
  await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
  log('EN branches:', JSON.stringify(await page.locator('.record-section table tbody tr').allTextContents()));
  // Arabic legal name setting: turn on, clear Arabic, save -> refused; message shown?
  const arInput = page.locator('[data-field="legalNameAr"] input');
  await arInput.fill('');
  await page.locator('[data-field="arabicLegalNameRequired"] input').check();
  log('EN setting on, Arabic blank, before save:', JSON.stringify(await fieldState(page, 'legalNameAr')));
  await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
  log('EN setting on, Arabic blank, after Ctrl+S:', JSON.stringify(await fieldState(page, 'legalNameAr')), 'setting', JSON.stringify(await fieldState(page, 'arabicLegalNameRequired')));
  await shot(page, '02-arabic-name-required-refused-en.jpg');
  const rec2 = await api(page, 'GET', `/api/tenancy/companies/${created}`);
  log('EN stored after refusal: legalNameAr', JSON.stringify(rec2.json?.legalNameAr), 'arabicRequired', rec2.json?.arabicLegalNameRequired);
  await arInput.fill('الناقد للتجارة ذ.م.م');
  await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
  const rec3 = await api(page, 'GET', `/api/tenancy/companies/${created}`);
  log('EN stored after fill+save: legalNameAr', JSON.stringify(rec3.json?.legalNameAr), 'arabicRequired', rec3.json?.arabicLegalNameRequired);
  // With the setting on, the list/profile still prints fine; turn off again
  console.log('CREATED_EN', created);
  await ctx.close();
}
// 2. Arabic admin, keyboard: new company; first field; branch with Arabic name only.
{
  const { ctx, page } = await session('admin.ar@alnoor.example', 'Demo-Pass-2026', 'ar');
  await page.goto(base + '/tenancy/companies');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.keyboard.press('Alt+n');
  await page.waitForTimeout(800);
  const first = await page.evaluate(() => document.activeElement?.closest('[data-field]')?.getAttribute('data-field'));
  log('AR new company first field:', first, '| order:', await order(page));
  await page.keyboard.type('شركة الناقد ' + tag + ' للتصنيع ذ.م.م');
  await page.keyboard.press('Tab');
  log('AR Tab from Arabic name lands on:', await page.evaluate(() => document.activeElement?.closest('[data-field]')?.getAttribute('data-field')));
  await page.keyboard.type(`Critic R10 AR ${tag} Manufacturing LLC`);
  await page.locator('[data-field="taxRegistrationNumber"] input').fill('123');
  await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
  log('AR TRN 123 refusal:', JSON.stringify(await fieldState(page, 'taxRegistrationNumber')));
  log('AR setting label:', JSON.stringify(await fieldState(page, 'arabicLegalNameRequired')));
  await shot(page, '03-trn-refused-ar.jpg');
  await page.locator('[data-field="taxRegistrationNumber"] input').fill('');
  await page.keyboard.press('Control+s');
  await page.waitForTimeout(2000);
  const created = new URL(page.url()).searchParams.get('open');
  const fname = await page.evaluate(() => document.activeElement?.getAttribute('name'));
  log('AR after save, focus on', fname, JSON.stringify(await page.evaluate(() => document.activeElement?.value)));
  await page.keyboard.press('Control+a'); await page.keyboard.type('فرع المصفح');
  await page.keyboard.press('Control+s');
  await page.waitForTimeout(1500);
  log('AR branches under company:', JSON.stringify(await page.locator('.record-section table tbody tr').allTextContents()));
  const br = await api(page, 'GET', `/api/tenancy/branches?filter=${encodeURIComponent(`companyId eq '${created}'`)}&take=10`);
  log('AR branch records:', JSON.stringify(br.json?.items?.map((b) => ({ code: b.code, nameEn: b.nameEn, nameAr: b.nameAr }))));
  await shot(page, '04-company-branch-keyboard-ar.jpg');
  console.log('CREATED_AR', created);
  // Arabic list overflow / clipping
  await page.goto(base + '/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  log('AR list overflow:', await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth), 'dir', await page.evaluate(() => document.documentElement.dir));
  await ctx.close();
}
await browser.close();
