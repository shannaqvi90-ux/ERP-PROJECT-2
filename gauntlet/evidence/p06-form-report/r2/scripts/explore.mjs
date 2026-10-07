import { chromium } from 'playwright-core';
import fs from 'node:fs';
const BASE = process.env.BASE || 'http://localhost:20650';
const OUT = '/home/shan/evidence-staging/p06-form-report/r2';
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
const shot = async (page, name) => { await page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 65 }); note('  shot', name); };
const step = process.argv[2] || 'all';
const focused = (page) => page.evaluate(() => { const a = document.activeElement; return a ? `${a.tagName} ${a.getAttribute('role') || ''} ${(a.textContent || a.value || '').slice(0, 40)} field=${a.closest('[data-field]')?.getAttribute('data-field') || ''}` : null; });

if (step === 'all' || step === 'form') {
  const { ctx, page } = await session('admin@alnoor.example');
  await page.goto(BASE + '/tenancy/companies');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  // open first record by keyboard: focus grid, Enter
  await page.locator('table[role=grid] tbody tr').first().click();
  const phone = page.locator('[data-field="phone"] input');
  await phone.waitFor();
  note('url', page.url());
  note('form title', await page.locator('.record-header h2').textContent());
  note('sections/tabs', await page.locator('.record-form legend, .record-form [role=tab]').allTextContents());
  note('field kinds', await page.locator('.record-form [data-field]').evaluateAll(els => els.map(e => e.getAttribute('data-field') + ':' + (e.querySelector('input,select,textarea')?.tagName.toLowerCase() + '/' + (e.querySelector('input')?.type || '') + (e.querySelector('[role=combobox]') ? '/combobox' : '')))));
  await shot(page, '01-company-form-en');
  const original = await phone.inputValue();
  await phone.click();
  await page.keyboard.type('+971 4 555 7777');
  note('header after edit', (await page.locator('.record-header').textContent()).slice(0, 200));
  await page.keyboard.press('Escape');
  await page.waitForTimeout(300);
  note('leave dialog after Escape', await page.locator('[role=alertdialog], [role=dialog]').allTextContents());
  if (await page.locator('[role=alertdialog], [role=dialog]').count()) { await shot(page, '02-leave-warning-en'); await page.keyboard.press('Escape'); }
  await page.waitForTimeout(300);
  // in-app navigation with dirty form
  await page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link').filter({ hasText: /Branches/ }).first().click();
  await page.waitForTimeout(500);
  note('url after nav attempt', page.url(), 'dialogs', await page.locator('[role=alertdialog], [role=dialog]').allTextContents());
  if (await page.locator('[role=alertdialog], [role=dialog]').count()) { await page.keyboard.press('Escape'); await page.waitForTimeout(300); }
  note('still on form', page.url(), await phone.inputValue().catch(() => 'gone'));
  await page.keyboard.press('Alt+KeyZ');
  await page.waitForTimeout(200);
  note('after Alt+Z', await phone.inputValue(), 'orig', original);
  // server validation
  const email = page.locator('[data-field="email"] input');
  await email.fill('not-an-email');
  await page.keyboard.press('Control+KeyS');
  await page.waitForTimeout(1200);
  note('errors', await page.locator('.record-form .field-error, .record-form [role=alert]').allTextContents());
  note('focused after refused save', await focused(page));
  await shot(page, '03-server-validation-en');
  await page.keyboard.press('Alt+KeyZ');
  // save by keyboard
  await phone.fill('+971 4 555 7778');
  await page.keyboard.press('Control+KeyS');
  await page.waitForTimeout(1200);
  note('after save notice', await page.locator('.record-form .notice').allTextContents());
  await phone.fill(original);
  await page.keyboard.press('Control+Enter');
  await page.waitForTimeout(1200);
  note('restored', await phone.inputValue(), await page.locator('.record-form .notice').allTextContents());
  // next / previous
  const t0 = await page.locator('.record-header h2').textContent();
  await page.keyboard.press('Alt+PageDown');
  await page.waitForTimeout(900);
  const t1 = await page.locator('.record-header h2').textContent();
  await page.keyboard.press('Alt+PageUp');
  await page.waitForTimeout(900);
  note('next/prev', t0, '->', t1, '->', await page.locator('.record-header h2').textContent());
  // print by keyboard
  await page.keyboard.press('Alt+KeyR');
  await page.waitForTimeout(300);
  note('print menu items', await page.getByRole('menuitem').allTextContents(), 'focused', await focused(page));
  await page.keyboard.press('Escape');
  await ctx.close();
}

if (step === 'all' || step === 'viewer') {
  const { ctx, page } = await session('viewer@alnoor.example');
  await page.goto(BASE + '/tenancy/companies');
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.locator('[data-field="phone"] input').waitFor();
  note('viewer header', (await page.locator('.record-form').first().textContent()).slice(0, 300));
  note('viewer phone disabled', await page.locator('[data-field="phone"] input').isDisabled());
  note('viewer save button count', await page.locator('.record-form button[type=submit]').count());
  await page.keyboard.press('Control+KeyS');
  await page.waitForTimeout(500);
  note('viewer after Ctrl+S notices', await page.locator('.record-form .notice, .record-form [role=alert]').allTextContents());
  await shot(page, '04-company-form-viewer-readonly');
  await ctx.close();
}

if (step === 'all' || step === 'arabic') {
  const { ctx, page } = await session('admin.ar@alnoor.example');
  await page.goto(BASE + '/tenancy/companies');
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.locator('[data-field="phone"] input').waitFor();
  note('ar dir', await page.evaluate(() => document.documentElement.dir), await page.locator('.record-header h2').textContent());
  await page.locator('[data-field="phone"] input').fill('+971 4 555 0000');
  await shot(page, '05-company-form-ar-rtl-dirty');
  await page.keyboard.press('Alt+KeyZ');
  await page.keyboard.press('Alt+KeyR');
  await page.waitForTimeout(300);
  note('ar record print menu', await page.getByRole('menuitem').allTextContents());
  const hrefs = await page.getByRole('menuitem').evaluateAll(es => es.map(e => e.getAttribute('href')));
  note('ar record print hrefs', hrefs);
  for (const h of hrefs.filter(Boolean)) {
    const u = new URL(BASE + h);
    if (u.searchParams.get('format') === 'pdf') {
      const r = await page.request.get(BASE + h);
      fs.writeFileSync(`${OUT}/company-profile-${u.searchParams.get('language')}.pdf`, await r.body());
      note('saved company profile', u.searchParams.get('language'), r.status(), r.headers()['content-disposition']);
    }
  }
  await page.keyboard.press('Escape');
  // reports screen
  await page.goto(BASE + '/reports/catalog');
  await page.waitForTimeout(1200);
  note('reports page text', (await page.locator('main').textContent()).slice(0, 600));
  await shot(page, '06-reports-catalog-ar');
  await ctx.close();
}

if (step === 'all' || step === 'listprint') {
  const { ctx, page } = await session('admin.ar@alnoor.example');
  await page.goto(BASE + '/identity/roles');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.keyboard.press('Alt+Shift+KeyR');
  await page.waitForTimeout(400);
  note('list print menu (Alt+Shift+R)', await page.getByRole('menuitem').allTextContents(), 'focused', await focused(page));
  const hrefs = await page.getByRole('menuitem').evaluateAll(es => es.map(e => e.getAttribute('href')));
  note('list print hrefs', hrefs);
  await shot(page, '07-list-print-menu-ar');
  for (const h of hrefs) {
    const u = new URL(BASE + h);
    const r = await page.request.get(BASE + h);
    const f = `roles-list-${u.searchParams.get('language')}.${u.searchParams.get('format')}`;
    fs.writeFileSync(`${OUT}/${f}`, await r.body());
    note('saved', f, r.status(), r.headers()['content-type']);
  }
  await page.keyboard.press('Escape');
  await ctx.close();
}
fs.writeFileSync(`${OUT}/explore-${step}.log`, log.join('\n'));
await browser.close();
