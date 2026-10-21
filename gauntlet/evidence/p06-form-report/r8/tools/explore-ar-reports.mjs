// Critic p06 r8: Arabic form, viewer read-only, reports screen (EN+AR), exports.
import { launch } from '/home/shan/critic/p06-form-report-r8-demo/gauntlet/compare/lib/browser.mjs';
import fs from 'node:fs';
const BASE = 'http://localhost:20650', OUT = '/home/shan/evidence-staging/p06-form-report/r8/browser';
const log = (...a) => console.log(...a);
const browser = await launch();
async function signIn(email, w = 1400, h = 900) {
  const ctx = await browser.newContext({ viewport: { width: w, height: h }, acceptDownloads: true });
  const page = await ctx.newPage();
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.locator('nav').first().waitFor();
  await page.waitForTimeout(800);
  return page;
}
// Arabic admin
{
  const page = await signIn('admin.ar@alnoor.example');
  log('ar html dir', await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang));
  await page.goto(BASE + '/tenancy/companies');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.locator('table[role=grid] tbody tr').first().click();
  const phone = page.locator('[data-field="phone"] input'); await phone.waitFor();
  log('ar form header', await page.locator('.record-header').textContent());
  log('ar phone dir', await phone.evaluate(e => getComputedStyle(e).direction), 'value', await phone.inputValue());
  log('ar labels', (await page.$$eval('[data-field] label', l => l.slice(0, 8).map(x => x.textContent.trim()))).join(' | '));
  await phone.click(); await page.keyboard.type('971505550199'); await page.waitForTimeout(800);
  log('ar 12-key: shown', await phone.inputValue(), 'notice', await page.locator('[data-field="phone"] .notice').textContent().catch(() => null));
  await page.screenshot({ path: OUT + '/03-ar-form-12key-saved.jpg', type: 'jpeg', quality: 70 });
  await page.keyboard.press('Alt+KeyZ'); await page.waitForTimeout(800);
  log('ar after undo', await phone.inputValue());
  const email = page.locator('[data-field="email"] input');
  await email.click(); await page.keyboard.press('Control+a'); await page.keyboard.type('xx'); await page.keyboard.press('Control+s'); await page.waitForTimeout(800);
  log('ar server error', (await page.locator('[data-field="email"]').textContent()).replace(/\s+/g, ' '));
  await page.keyboard.press('Escape'); await page.waitForTimeout(300);
  log('ar leave dialog', await page.getByRole('dialog').getByRole('button').allTextContents().catch(() => []));
  await page.screenshot({ path: OUT + '/04-ar-leave-dialog.jpg', type: 'jpeg', quality: 70 });
  await page.getByRole('dialog').getByRole('button').nth(1).click(); await page.waitForTimeout(500);
  // Print menu
  await page.goto(BASE + '/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().click(); await phone.waitFor();
  await page.keyboard.press('Alt+KeyR'); await page.waitForTimeout(400);
  const items = await page.getByRole('menuitem').allTextContents();
  const focused = await page.evaluate(() => document.activeElement?.textContent);
  log('ar print menu', JSON.stringify(items), 'focused', focused);
  const dl = page.waitForEvent('download'); await page.keyboard.press('Enter'); const d = await dl;
  await d.saveAs(OUT + '/company-profile-ar.pdf'); log('ar record pdf', d.suggestedFilename());
  // Reports screen in Arabic
  await page.goto(BASE + '/reports/catalog'); await page.waitForTimeout(1000);
  const reports = await page.locator('main button').allTextContents();
  log('ar catalog buttons', JSON.stringify(reports.slice(0, 20)));
  await page.screenshot({ path: OUT + '/05-ar-reports-catalog.jpg', type: 'jpeg', quality: 70 });
  await page.context().close();
}
// English admin: reports
{
  const page = await signIn('admin@alnoor.example');
  await page.goto(BASE + '/reports/catalog'); await page.waitForTimeout(1000);
  const names = await page.locator('main button').allTextContents();
  log('en catalog buttons', JSON.stringify(names.slice(0, 20)));
  // keyboard: tab to first report
  const rep = page.getByRole('button', { name: /Users by role|Role summary|role/i }).first();
  log('role report button', await rep.textContent().catch(() => 'none'));
  await rep.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(400);
  log('focus after Enter', await page.evaluate(() => document.activeElement?.closest('[data-field]')?.getAttribute('data-field') || document.activeElement?.tagName));
  log('params', JSON.stringify(await page.$$eval('[data-field]', els => els.map(e => e.getAttribute('data-field') + ':' + (e.querySelector('input,select,textarea')?.type || '?')))));
  await page.keyboard.press('Control+Enter'); await page.waitForTimeout(1500);
  const doc = page.getByTestId('report-document');
  log('en doc groups', await doc.locator('.report-group').count(), 'totals text', (await doc.locator('tfoot, .report-total, .report-group-total').allTextContents()).slice(0, 6).join(' / ').replace(/\s+/g, ' '));
  await page.screenshot({ path: OUT + '/06-en-report-grouped-totals.jpg', type: 'jpeg', quality: 70 });
  // Branch directory in Arabic + exports
  await page.getByRole('button', { name: /Branch directory/ }).click();
  await page.locator('[data-field="language"] select').selectOption('ar');
  await page.keyboard.press('Control+Enter'); await page.waitForTimeout(1500);
  log('branch dir ar: article dir', await doc.locator('article').getAttribute('dir'), 'groups', await doc.locator('.report-group').count());
  await page.screenshot({ path: OUT + '/07-ar-branch-directory.jpg', type: 'jpeg', quality: 70 });
  const links = await page.locator('a[href*="/api/reports/run/"]').evaluateAll(as => as.map(a => a.textContent.trim() + ' ' + a.getAttribute('href')));
  log('export links', JSON.stringify(links));
  for (const href of links.map(l => l.split(' ').pop())) {
    const r = await page.request.get(BASE + href);
    const b = await r.body();
    const ext = /format=(\w+)/.exec(href)?.[1];
    fs.writeFileSync(`${OUT}/branch-directory-ar.${ext}`, b);
    log('  ', ext, r.status(), r.headers()['content-type'], b.length);
  }
  await page.context().close();
}
// viewer
{
  const page = await signIn('viewer@alnoor.example');
  const writes = []; page.on('request', r => { const p = new URL(r.url()).pathname; if (r.method() !== 'GET' && p !== '/api/identity/me/preferences') writes.push(r.method() + ' ' + p); });
  await page.goto(BASE + '/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().click();
  const phone = page.locator('[data-field="phone"] input'); await phone.waitFor();
  const dis = await page.$$eval('[data-field] input,[data-field] select,[data-field] textarea', els => [els.filter(e => e.disabled || e.readOnly).length, els.length]);
  log('viewer disabled fields', JSON.stringify(dis), 'readonly badge', await page.getByTestId('record-read-only').count(), 'save buttons', await page.locator('.record-form').getByRole('button', { name: 'Save', exact: true }).count());
  await phone.click({ force: true }).catch(() => {}); await page.keyboard.type('971505550199'); for (const k of ['Enter', 'Control+s', 'Alt+KeyZ']) await page.keyboard.press(k);
  await page.waitForTimeout(800);
  log('viewer writes', JSON.stringify(writes));
  const r = await page.request.put(BASE + '/api/tenancy/companies/018dca08-f081-7b8a-bbf4-180f0d255d0c', { data: { phone: '+971 50 555 0199' }, headers: { 'X-Erp-Request': '1' } });
  log('viewer direct PUT', r.status());
  await page.screenshot({ path: OUT + '/08-viewer-read-only.jpg', type: 'jpeg', quality: 70 });
}
await browser.close();
