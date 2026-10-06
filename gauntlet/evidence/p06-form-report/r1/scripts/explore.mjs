import { chromium } from 'playwright-core';
import fs from 'node:fs';
const BASE = process.env.BASE || 'http://localhost:20650';
const OUT = process.env.OUT || '/home/shan/evidence-staging/p06-form-report/r1';
const exe = '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
const log = [];
const note = (...a) => { const s = a.map(x => typeof x === 'string' ? x : JSON.stringify(x)).join(' '); log.push(s); console.log(s); };
const browser = await chromium.launch({ executablePath: exe });
async function session(email) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, acceptDownloads: true });
  const page = await ctx.newPage();
  page.on('dialog', d => { note('  [native dialog]', d.type(), d.message()); d.dismiss(); });
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]').fill(email);
  await page.locator('input[name="password"]').fill('Demo-Pass-2026');
  await page.keyboard.press('Enter');
  await page.locator('nav[aria-label]').first().waitFor({ timeout: 30000 });
  return { ctx, page };
}
const shot = async (page, name) => { await page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 70 }); note('  shot', name); };
const step = process.argv[2] || 'all';

if (step === 'all' || step === 'form') {
  const { ctx, page } = await session('admin@alnoor.example');
  await page.goto(BASE + '/tenancy/companies');
  await page.locator('table[role=grid] tbody tr').first().click();
  const phone = page.locator('[data-field="phone"] input');
  await phone.waitFor();
  note('form title', await page.locator('.record-header h2').textContent());
  note('sections', await page.locator('.record-body h3, .record-body legend').allTextContents());
  note('field types', await page.locator('.record-body [data-field]').evaluateAll(els => els.map(e => e.getAttribute('data-field') + ':' + (e.querySelector('input,select,textarea')?.tagName + '/' + (e.querySelector('input')?.type || '')))));
  await shot(page, '01-company-form-en');
  // focus after click: is whole value selected?
  await phone.click();
  note('selection after click', await phone.evaluate(e => [e.selectionStart, e.selectionEnd, e.value.length]));
  const original = await phone.inputValue();
  await page.keyboard.type('+971 4 555 7777');
  note('dirty marker', await page.locator('.record-header').textContent());
  // Escape asks
  await page.keyboard.press('Escape');
  note('after Escape dialog visible', await page.locator('.confirm-dialog').isVisible().catch(() => false));
  if (await page.locator('.confirm-dialog').isVisible().catch(() => false)) { await shot(page, '02-leave-warning-en'); await page.getByRole('button', { name: /Keep/ }).click(); }
  // in-app navigation
  await page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link', { name: 'Branches' }).click();
  await page.waitForTimeout(500);
  note('url after nav attempt (dismissed)', page.url());
  // browser reload / close
  // Alt+Z discard
  await page.keyboard.press('Alt+KeyZ');
  note('after Alt+Z value', await phone.inputValue(), 'orig', original);
  // server validation: bad email, empty legal name
  const email = page.locator('[data-field="email"] input');
  await email.fill('not-an-email');
  const legal = page.locator('[data-field="legalNameEn"] input');
  const legalOrig = await legal.inputValue();
  await legal.fill('');
  await page.keyboard.press('Control+KeyS');
  await page.waitForTimeout(1000);
  note('errors', await page.locator('.record-body .field-error, .record-body [role=alert], .record-form .alert').allTextContents());
  note('focused field', await page.evaluate(() => document.activeElement?.closest('[data-field]')?.getAttribute('data-field')));
  await shot(page, '03-server-validation-en');
  await page.keyboard.press('Alt+KeyZ');
  // stale version conflict: save in another tab
  const p2 = await ctx.newPage();
  await p2.goto(page.url());
  await p2.locator('[data-field="phone"] input').waitFor();
  await p2.locator('[data-field="phone"] input').fill('+971 4 555 1111');
  await p2.keyboard.press('Control+KeyS');
  await p2.locator('.record-form .notice').waitFor();
  await p2.close();
  await phone.fill('+971 4 555 2222');
  await page.keyboard.press('Control+KeyS');
  await page.waitForTimeout(1000);
  note('conflict message', await page.locator('.record-form .alert').allTextContents());
  await shot(page, '04-conflict-en');
  // reload latest and restore
  const reloadBtn = page.getByRole('button', { name: /latest/i });
  if (await reloadBtn.count()) await reloadBtn.click();
  await page.waitForTimeout(800);
  await phone.fill(original);
  await page.keyboard.press('Control+KeyS');
  await page.waitForTimeout(800);
  note('restored', await phone.inputValue());
  // next / previous
  await page.keyboard.press('Alt+PageDown');
  await page.waitForTimeout(800);
  note('after Alt+PageDown', await page.locator('.record-header h2').textContent(), await page.locator('.record-position').textContent().catch(() => null));
  // print menu by keyboard
  await page.keyboard.press('Alt+KeyR');
  await page.waitForTimeout(300);
  note('print menu items', await page.getByRole('menuitem').allTextContents());
  await page.keyboard.press('Escape');
  await ctx.close();
}

if (step === 'all' || step === 'viewer') {
  const { ctx, page } = await session('viewer@alnoor.example');
  await page.goto(BASE + '/tenancy/companies');
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.locator('[data-field="phone"] input').waitFor();
  note('viewer readonly badge', await page.locator('[data-testid=record-read-only]').textContent().catch(() => null));
  note('viewer phone disabled', await page.locator('[data-field="phone"] input').isDisabled(), 'readonly', await page.locator('[data-field="phone"] input').getAttribute('readonly'));
  note('viewer save button count', await page.locator('.record-form button[type=submit]').count());
  await shot(page, '05-company-form-viewer-readonly');
  await ctx.close();
}

if (step === 'all' || step === 'arabic') {
  const { ctx, page } = await session('admin.ar@alnoor.example');
  await page.goto(BASE + '/tenancy/companies');
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.locator('[data-field="phone"] input').waitFor();
  note('ar dir', await page.evaluate(() => document.documentElement.dir), await page.locator('.record-header h2').textContent());
  await page.locator('[data-field="phone"] input').fill('x');
  await shot(page, '06-company-form-ar-rtl');
  await page.keyboard.press('Alt+KeyZ');
  await page.keyboard.press('Alt+KeyR');
  await page.waitForTimeout(300);
  note('ar print menu', await page.getByRole('menuitem').allTextContents());
  const arItem = page.getByRole('menuitem').nth(1);
  const href = await arItem.getAttribute('href');
  note('ar href', href);
  const pdf = await page.request.get(BASE + href);
  fs.writeFileSync(`${OUT}/company-profile-ar.pdf`, await pdf.body());
  await page.keyboard.press('Escape');
  // reports screen in Arabic
  await page.goto(BASE + '/reports/catalog');
  await page.waitForTimeout(800);
  note('reports catalog', await page.locator('.reports-list li').allTextContents());
  await page.locator('.reports-list button').nth(1).click();
  await page.keyboard.press('Control+Enter');
  await page.getByTestId('report-document').locator('article').waitFor();
  await page.waitForTimeout(500);
  await shot(page, '07-report-branch-directory-ar');
  // users list print
  await page.goto(BASE + '/identity/users');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  const printBtn = page.locator('button[aria-haspopup=menu]').filter({ hasText: /طباعة|Print/ }).first();
  note('print buttons', await page.locator('button[aria-haspopup=menu]').allTextContents());
  await ctx.close();
}
fs.writeFileSync(`${OUT}/explore-${step}.log`, log.join('\n'));
await browser.close();
