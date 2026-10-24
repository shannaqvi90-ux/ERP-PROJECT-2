// Critic p02 r15: a user's walk through companies, branches and the switcher, by keyboard, in English and Arabic.
// Usage: node walk.mjs http://localhost:20250 <shots dir>
import { createRequire } from 'node:module';
const require = createRequire('/home/shan/critic/p02-tenancy-r15/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
const base = process.argv[2]; const shots = process.argv[3];
const exe = '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
const browser = await chromium.launch({ executablePath: exe });
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
const tag = `${Date.now() % 100000}`;
async function start(lang, email) {
  const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 }, baseURL: base });
  const page = await ctx.newPage();
  page.on('pageerror', e => log('PAGE ERROR', e.message));
  await page.goto('/');
  await page.evaluate(l => { localStorage.clear(); localStorage.setItem('erp.language', l); }, lang);
  await page.goto('/');
  await page.locator('input[name="email"]').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.getByTestId('workplace').waitFor({ timeout: 30000 });
  return { ctx, page };
}
const shot = (page, name) => page.screenshot({ path: `${shots}/${name}.jpg`, type: 'jpeg', quality: 70 });
const overflow = page => page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, client: document.documentElement.clientWidth }));
const focused = page => page.evaluate(() => { const e = document.activeElement; return e ? `${e.tagName} name=${e.getAttribute('name')} field=${e.closest('[data-field]')?.getAttribute('data-field')}` : 'none'; });

// 1. English admin, keyboard only: G C, Alt+N, both legal names, code, then the extra fields by Tab, Ctrl+S, branch line, Enter.
{
  const { ctx, page } = await start('en', 'admin@alnoor.example');
  log('EN workplace', await page.getByTestId('workplace').innerText());
  await page.keyboard.press('g'); await page.keyboard.press('c');
  await page.waitForURL(/tenancy\/companies/);
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  log('companies list header', (await page.locator('.list-count, [data-testid=list-count]').first().innerText().catch(() => '?')));
  const rows = await page.locator('table[role=grid] tbody tr').allInnerTexts();
  log('companies list rows (first 12):', JSON.stringify(rows.slice(0, 12).map(r => r.replace(/\s+/g, ' ').slice(0, 120))));
  await shot(page, '01-companies-list-en');
  await page.keyboard.press('Alt+KeyN');
  log('after Alt+N focus', await focused(page));
  await page.keyboard.type(`Critic Trading ${tag} LLC`); await page.keyboard.press('Tab');
  await page.keyboard.type(`شركة الناقد ${tag} للتجارة ذ.م.م`); await page.keyboard.press('Tab');
  await page.keyboard.type(`cr${tag}`);
  // Walk the rest of the form by Tab and record the field order.
  const order = [];
  for (let i = 0; i < 30; i++) { await page.keyboard.press('Tab'); order.push(await focused(page)); }
  log('tab order after code:', JSON.stringify(order));
  // Fill the statutory fields directly to check them.
  const fill = async (field, value) => { const l = page.locator(`[data-field="${field}"] input, [data-field="${field}"] select, [data-field="${field}"] textarea`).first(); if (await l.count()) { const tagName = await l.evaluate(e => e.tagName); if (tagName === 'SELECT') await l.selectOption({ label: value }).catch(async () => l.selectOption(value)); else await l.fill(value); return true; } return false; };
  log('fields on the form', JSON.stringify(await page.locator('[data-field]').evaluateAll(es => es.map(e => e.getAttribute('data-field')))));
  log('trade licence', await fill('tradeLicenceNumber', `CN-${tag}`));
  log('trn', await fill('taxRegistrationNumber', '100-2345 6789-0003'));
  log('city', await fill('city', 'Dubai'));
  await page.keyboard.press('Control+KeyS');
  await page.locator('.record-form .notice').waitFor({ timeout: 15000 }).catch(() => {});
  log('notice', await page.locator('.record-form .notice').innerText().catch(() => 'none'));
  log('header', await page.locator('.record-header h2').innerText().catch(() => 'none'));
  log('focus after save', await focused(page), await page.locator('input[name="branchNameEn"]').inputValue().catch(() => '-'));
  await page.locator('input[name="branchNameEn"]').fill('Critic Trading - Head office');
  await page.locator('input[name="branchNameAr"]').fill('المكتب الرئيسي');
  await page.locator('input[name="branchCode"]').fill('hq');
  await page.locator('input[name="branchCode"]').press('Enter');
  await page.waitForTimeout(1500);
  log('branch rows', JSON.stringify(await page.locator('.record-section table tbody tr').allInnerTexts()));
  log('focus after branch', await focused(page));
  await shot(page, '02-new-company-with-branch-en');
  log('overflow', JSON.stringify(await overflow(page)));
  // Switch to it by keyboard.
  await page.keyboard.press('Alt+KeyC');
  await page.locator('.workplace-popover input').waitFor();
  await page.keyboard.type(`CR${tag}`);
  await page.waitForTimeout(500);
  await shot(page, '03-switcher-filter-en');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(800);
  log('workplace after switch', await page.getByTestId('workplace').innerText());
  await page.reload(); await page.getByTestId('workplace').waitFor();
  log('workplace after reload', await page.getByTestId('workplace').innerText());
  // Help lists keys.
  await page.keyboard.press('Escape');
  await page.locator('body').click({ position: { x: 5, y: 300 } }).catch(() => {});
  await page.keyboard.press('g'); await page.keyboard.press('b');
  await page.waitForURL(/tenancy\/branches/).catch(() => {});
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  log('branches URL', page.url(), 'rows', await page.locator('table[role=grid] tbody tr').count());
  await shot(page, '04-branches-list-en');
  await ctx.close();
}
// 2. Arabic admin.
{
  const { ctx, page } = await start('ar', 'admin.ar@alnoor.example');
  log('AR dir', await page.locator('html').getAttribute('dir'), 'workplace', await page.getByTestId('workplace').innerText());
  await page.keyboard.press('g'); await page.keyboard.press('c');
  await page.waitForURL(/tenancy\/companies/);
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  log('AR overflow', JSON.stringify(await overflow(page)));
  await shot(page, '05-companies-list-ar');
  await page.keyboard.press('Alt+KeyN');
  log('AR after Alt+N focus', await focused(page));
  await page.keyboard.type(`مصنع الناقد ${tag} ذ.م.م`); await page.keyboard.press('Tab');
  log('AR second field', await focused(page));
  await page.keyboard.type(`Critic Factory ${tag} LLC`); await page.keyboard.press('Tab');
  await page.keyboard.type(`cf${tag}`);
  const trn = page.locator('[data-field="taxRegistrationNumber"] input');
  if (await trn.count()) await trn.fill('١٠٠٣٤٥٦٧٨٩٠٠٠٠٣');
  await page.keyboard.press('Control+KeyS');
  await page.locator('.record-form .notice').waitFor({ timeout: 15000 }).catch(() => {});
  log('AR notice', await page.locator('.record-form .notice').innerText().catch(() => 'none'));
  log('AR TRN shown', await trn.inputValue().catch(() => '-'));
  log('AR focus after save', await focused(page), await page.locator('input[name="branchNameAr"], input[name="branchNameEn"]').first().inputValue().catch(() => '-'));
  await page.keyboard.type('الشارقة');
  await page.locator('input[name="branchCode"]').fill('shj');
  await page.locator('input[name="branchCode"]').press('Enter');
  await page.waitForTimeout(1500);
  log('AR branch rows', JSON.stringify(await page.locator('.record-section table tbody tr').allInnerTexts()));
  await shot(page, '06-new-company-with-branch-ar');
  log('AR form overflow', JSON.stringify(await overflow(page)));
  await page.keyboard.press('Alt+KeyC');
  await page.locator('.workplace-popover input').waitFor();
  await page.keyboard.type('الشارقة');
  await page.waitForTimeout(500);
  log('AR switcher options', JSON.stringify((await page.locator('.workplace-popover [role="option"]').allInnerTexts()).slice(0, 8)));
  await shot(page, '07-switcher-ar');
  await page.keyboard.press('Escape');
  await ctx.close();
}
// 3. Read-only viewer.
{
  const { ctx, page } = await start('en', 'viewer@alnoor.example');
  log('viewer workplace', await page.getByTestId('workplace').innerText());
  await page.goto('/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  log('viewer companies', JSON.stringify(await page.locator('table[role=grid] tbody tr').allInnerTexts()));
  log('viewer New button', await page.getByRole('button', { name: /new/i }).count());
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.waitForTimeout(1000);
  log('viewer editable inputs', await page.locator('.record-form input:not([readonly]):not([disabled])').count(), 'save buttons', await page.getByRole('button', { name: /save/i }).count());
  await shot(page, '08-viewer-read-only-en');
  await ctx.close();
}
await browser.close();
