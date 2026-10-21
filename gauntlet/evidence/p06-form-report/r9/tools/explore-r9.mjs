// Critic p06 r9: the form and report framework in a real browser (Chromium), English and Arabic, by keyboard.
import { launch } from '/home/shan/critic/p06-form-report-r9-demo/gauntlet/compare/lib/browser.mjs';
import fs from 'node:fs';
const BASE = process.env.BASE || 'http://localhost:20650', OUT = '/home/shan/evidence-staging/p06-form-report/r9/browser';
const only = process.env.ONLY || 'form,reports,ar,list,viewer';
const log = (...a) => console.log(...a);
const shot = (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 62 });
const browser = await launch();
async function signIn(email, w = 1400, h = 900) {
  const ctx = await browser.newContext({ viewport: { width: w, height: h }, acceptDownloads: true });
  const page = await ctx.newPage();
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.locator('nav[aria-label]').first().waitFor();
  await page.waitForTimeout(600);
  return page;
}
if (only.includes('form')) {
  const page = await signIn('admin@alnoor.example');
  const puts = [];
  page.on('request', r => { if (r.method() === 'PUT' && r.url().includes('/api/tenancy/companies/')) puts.push(r.postDataJSON()?.phone ?? Object.keys(r.postDataJSON() || {}).join(',')); });
  await page.keyboard.press('g'); await page.keyboard.press('c');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  log('G C ->', page.url());
  await page.locator('table[role=grid]').focus().catch(() => {});
  await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
  const phone = page.locator('[data-field="phone"] input');
  await phone.waitFor();
  log('opened by keyboard:', await page.locator('.record-header h2').textContent(), page.url());
  log('fields:', (await page.$$eval('[data-field]', els => els.map(e => { const i = e.querySelector('input,select,textarea'); return `${e.getAttribute('data-field')}:${i ? (i.tagName.toLowerCase() + (i.type ? '/' + i.type : '')) : '?'}`; }))).join(' '));
  log('sections/tabs:', await page.$$eval('.record-form [role=tab], .record-form fieldset legend, .record-form h3', els => els.map(e => e.textContent.trim()).join(' | ')));
  const original = await phone.inputValue();
  await phone.click(); await page.keyboard.type('971505550199'); await page.waitForTimeout(900);
  log('12-key entry: shown', await phone.inputValue(), 'PUTs', JSON.stringify(puts));
  await shot(page, '02-en-form-12key-saved');
  await page.keyboard.press('Alt+KeyZ'); await page.waitForTimeout(900);
  log('after Alt+Z (undo):', await phone.inputValue(), 'PUTs', JSON.stringify(puts));
  for (const entry of ['9715055501', '97150555019']) {
    await page.locator('.record-header h2').click(); await phone.click();
    const before = puts.length; await page.keyboard.type(entry); await page.waitForTimeout(700);
    const shown = await phone.inputValue(); await page.keyboard.press('Enter'); await page.waitForTimeout(900);
    log(`malformed ${entry}: shown ${shown}; PUTs ${JSON.stringify(puts.slice(before))}; field "${(await page.locator('[data-field="phone"]').textContent()).replace(/\s+/g, ' ').slice(0, 160)}"`);
    if (puts.length > before) { await page.locator('.record-header h2').click(); await phone.click(); await page.keyboard.press('Control+a'); await page.keyboard.type(original.replace(/\s/g, '')); await page.waitForTimeout(600); await page.keyboard.press('Enter'); await page.waitForTimeout(700); }
    else { await page.keyboard.press('Alt+KeyZ'); await page.waitForTimeout(400); }
  }
  log('phone restored to', await phone.inputValue(), '(original', original, ')');
  const email = page.locator('[data-field="email"] input');
  await email.click(); await page.keyboard.press('Control+a'); await page.keyboard.type('not-an-email'); await page.keyboard.press('Control+s'); await page.waitForTimeout(900);
  log('server error on field:', (await page.locator('[data-field="email"]').textContent()).replace(/\s+/g, ' '), 'focused', await email.evaluate(e => e === document.activeElement));
  let bu = false; page.on('dialog', async d => { bu = d.type(); await d.dismiss(); });
  await page.reload().catch(() => {}); await page.waitForTimeout(500);
  log('reload while dirty raised:', bu);
  await page.keyboard.press('Escape'); await page.waitForTimeout(300);
  log('Escape (leave) dialog:', await page.getByRole('dialog').getByRole('button').allTextContents().catch(() => []));
  await page.keyboard.press('Escape'); await page.keyboard.press('Alt+KeyZ'); await page.waitForTimeout(400);
  log('Alt+Z discard -> email', await email.inputValue());
  const t1 = await page.locator('.record-header h2').textContent();
  await page.keyboard.press('Alt+PageDown'); await page.waitForTimeout(700);
  const t2 = await page.locator('.record-header h2').textContent();
  await page.keyboard.press('Alt+PageUp'); await page.waitForTimeout(700);
  log('next/prev:', t1, '->', t2, '->', await page.locator('.record-header h2').textContent());
  log('PUTs total', JSON.stringify(puts));
  await page.context().close();
}
if (only.includes('reports')) {
  const page = await signIn('admin@alnoor.example');
  const popups = [];
  page.context().on('page', p => popups.push(p));
  await page.goto(BASE + '/reports/catalog'); await page.waitForTimeout(800);
  const rep = page.getByRole('button', { name: /Users by role/ }).first();
  await rep.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(400);
  log('params', JSON.stringify(await page.$$eval('[data-field]', els => els.map(e => e.getAttribute('data-field') + ':' + (e.querySelector('input,select,textarea')?.tagName.toLowerCase() || '?')))));
  const t0 = Date.now(); await page.keyboard.press('Control+Enter');
  const doc = page.getByTestId('report-document');
  await doc.locator('.report-count').waitFor({ timeout: 60000 });
  log('shown in', Date.now() - t0, 'ms; count text:', await doc.locator('.report-count').textContent(), '| groups', await doc.locator('.report-group').count());
  const printAll = page.locator('[data-testid="report-print-all"]');
  log('print-all link', await printAll.count(), await printAll.textContent().catch(() => ''), await printAll.getAttribute('href').catch(() => ''));
  log('plain Print button', await page.getByRole('button', { name: 'Print', exact: true }).count());
  await shot(page, '03-en-report-print-all-offer');
  // group by another column: does the print-all PDF follow it?
  const gb = page.locator('[data-field="groupBy"] select');
  if (await gb.count()) {
    const opts = await gb.locator('option').evaluateAll(os => os.map(o => o.value));
    log('groupBy options', JSON.stringify(opts), 'current', await gb.inputValue());
    const other = opts.find(o => o && o !== (await_dummy => 0) && o !== '');
    const pick = opts.filter(o => o)[1] ?? opts.filter(o => o)[0];
    await gb.selectOption(pick); await page.keyboard.press('Control+Enter'); await page.waitForTimeout(3000);
    log(`after groupBy=${pick}: screen groups`, await doc.locator('.report-group').count(), 'first group', (await doc.locator('.report-group').first().textContent().catch(() => '')).slice(0, 60),
      '| print-all href', await printAll.getAttribute('href').catch(() => ''), '| pdf href', await page.locator('a[href*="format=pdf"]:not([data-testid])').first().getAttribute('href').catch(() => ''));
  }
  // Ctrl+P opens the whole PDF
  const t1 = Date.now();
  const before = popups.length;
  await page.locator('body').click({ position: { x: 5, y: 5 } }).catch(() => {});
  await page.keyboard.press('Control+KeyP'); await page.waitForTimeout(1500);
  if (popups.length > before) { const p = popups.at(-1); await p.waitForLoadState('load', { timeout: 120000 }).catch(() => {}); log('Ctrl+P opened', p.url(), 'after', Date.now() - t1, 'ms'); await p.close(); } else log('Ctrl+P opened nothing');
  // Arabic report document
  await page.locator('[data-field="language"] select').selectOption('ar');
  await page.keyboard.press('Control+Enter'); await page.waitForTimeout(3000);
  log('ar doc dir', await doc.locator('article').getAttribute('dir'), 'count', await doc.locator('.report-count').textContent(), 'print-all', await printAll.textContent().catch(() => ''));
  await page.context().close();
}
if (only.includes('ar')) {
  const page = await signIn('admin.ar@alnoor.example');
  log('ar html dir', await page.evaluate(() => document.documentElement.dir));
  await page.goto(BASE + '/reports/catalog'); await page.waitForTimeout(800);
  await page.locator('main button').filter({ hasText: /المستخدمون حسب الدور|حسب الدور/ }).first().focus().catch(() => {});
  log('ar catalog', JSON.stringify((await page.locator('main button').allTextContents()).slice(0, 8)));
  await page.keyboard.press('Enter'); await page.waitForTimeout(400);
  await page.keyboard.press('Control+Enter');
  const doc = page.getByTestId('report-document');
  await doc.locator('.report-count').waitFor({ timeout: 60000 }); await page.waitForTimeout(500);
  log('ar report:', await doc.locator('article').getAttribute('dir'), await doc.locator('.report-count').textContent(), '|', await page.locator('[data-testid="report-print-all"]').textContent().catch(() => 'no print-all'));
  await shot(page, '04-ar-report-print-all-offer');
  // company form in Arabic + print menu
  await page.goto(BASE + '/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.locator('table[role=grid] tbody tr').nth(3).click();
  const phone = page.locator('[data-field="phone"] input'); await phone.waitFor();
  log('ar form', await page.locator('.record-header h2').textContent(), 'phone dir', await phone.evaluate(e => getComputedStyle(e).direction));
  await page.keyboard.press('Alt+KeyR'); await page.waitForTimeout(400);
  log('ar print menu', JSON.stringify(await page.getByRole('menuitem').allTextContents()), 'focused', await page.evaluate(() => document.activeElement?.textContent));
  const dl = page.waitForEvent('download'); await page.keyboard.press('Enter'); const d = await dl;
  await d.saveAs(OUT + '/company-profile-4th-ar.pdf'); log('ar record pdf', d.suggestedFilename());
  await shot(page, '05-ar-company-form');
  await page.context().close();
}
if (only.includes('list')) {
  const page = await signIn('admin.ar@alnoor.example');
  await page.keyboard.press('g'); await page.keyboard.press('u');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.waitForTimeout(800);
  log('users list', page.url());
  await page.keyboard.press('Alt+Shift+KeyR'); await page.waitForTimeout(400);
  log('list print menu', JSON.stringify(await page.getByRole('menuitem').allTextContents()), 'focused', await page.evaluate(() => document.activeElement?.textContent));
  const t0 = Date.now();
  const dl = page.waitForEvent('download', { timeout: 180000 }); await page.keyboard.press('Enter');
  await page.waitForTimeout(1500); await shot(page, '06-ar-users-list-whole-pdf-wait');
  const busyText = await page.locator('[role=status], [aria-busy=true], .toast, .notice').allTextContents().catch(() => []);
  log('while the whole-list PDF is made (1.5 s in): status texts', JSON.stringify(busyText));
  const d = await dl; log('download started after', Date.now() - t0, 'ms', d.suggestedFilename());
  await d.saveAs(OUT + '/../api/users-list-ar-from-screen.pdf'); log('saved after', Date.now() - t0, 'ms');
  await page.context().close();
}
if (only.includes('viewer')) {
  const page = await signIn('viewer@alnoor.example');
  const writes = []; page.on('request', r => { const p = new URL(r.url()).pathname; if (r.method() !== 'GET' && p !== '/api/identity/me/preferences') writes.push(r.method() + ' ' + p); });
  await page.goto(BASE + '/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().click();
  const phone = page.locator('[data-field="phone"] input'); await phone.waitFor();
  const dis = await page.$$eval('[data-field] input,[data-field] select,[data-field] textarea', els => [els.filter(e => e.disabled || e.readOnly).length, els.length]);
  log('viewer disabled fields', JSON.stringify(dis), 'save buttons', await page.locator('.record-form').getByRole('button', { name: 'Save', exact: true }).count());
  await phone.click({ force: true }).catch(() => {}); await page.keyboard.type('971505550199'); for (const k of ['Enter', 'Control+s', 'Alt+KeyZ']) await page.keyboard.press(k);
  await page.waitForTimeout(800);
  log('viewer writes', JSON.stringify(writes));
  await page.context().close();
}
await browser.close();
