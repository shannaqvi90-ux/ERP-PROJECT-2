// Critic p06 r8: the form framework in a real browser, English, by keyboard. Logs to stdout.
import { launch } from '/home/shan/critic/p06-form-report-r8-demo/gauntlet/compare/lib/browser.mjs';
const BASE = process.env.BASE || 'http://localhost:20650', OUT = '/home/shan/evidence-staging/p06-form-report/r8/browser';
const log = (...a) => console.log(...a);
const browser = await launch();
async function signIn(email, w = 1400, h = 900) {
  const ctx = await browser.newContext({ viewport: { width: w, height: h }, acceptDownloads: true });
  const page = await ctx.newPage();
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.locator('nav[aria-label="Main navigation"]').waitFor();
  return page;
}
const page = await signIn('admin@alnoor.example');
const puts = [];
page.on('request', r => { if (r.method() === 'PUT' && r.url().includes('/api/tenancy/companies/')) puts.push(r.postDataJSON()?.phone); });
await page.goto(BASE + '/tenancy/companies');
await page.locator('table[role=grid] tbody tr').first().waitFor();
await page.locator('table[role=grid] tbody tr').first().click();
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
const phone = page.locator('[data-field="phone"] input');
await phone.waitFor();
log('opened by ArrowDown+Enter:', await page.locator('.record-header h2').textContent(), page.url());
// field types present
const fields = await page.$$eval('[data-field]', els => els.map(e => { const i = e.querySelector('input,select,textarea'); return `${e.getAttribute('data-field')}:${i ? (i.tagName.toLowerCase() + (i.type ? '/' + i.type : '')) : '?'}`; }));
log('fields:', fields.join(' '));
log('sections/tabs:', await page.$$eval('.record-form [role=tab], .record-form fieldset legend, .record-form h3', els => els.map(e => e.textContent.trim()).join(' | ')));
const original = await phone.inputValue();
log('original phone', original);
// The 12-key entry
for (const entry of ['971505550199', '505550199', '0505550199', '00971505550199']) {
  await page.locator('.record-header h2').click();
  await phone.click();
  const sel = await phone.evaluate(e => [e.selectionStart, e.selectionEnd, e.value]);
  const before = puts.length;
  await page.keyboard.type(entry);
  await page.waitForTimeout(800);
  const shown = await phone.inputValue();
  const note = page.locator('[data-field="phone"] .notice[role="status"]');
  const vis = await note.count() ? await note.evaluate(e => { const r = e.getBoundingClientRect(); return [Math.round(r.top), Math.round(r.bottom), innerHeight, e.textContent]; }) : null;
  const unsaved = /Unsaved/.test(await page.locator('.record-header').textContent());
  log(`entry ${entry}: selection ${JSON.stringify(sel)} -> shown ${shown}; PUTs ${JSON.stringify(puts.slice(before))}; unsaved=${unsaved}; field notice ${JSON.stringify(vis)}`);
  if (entry === '971505550199') await page.screenshot({ path: OUT + '/01-12key-saved-notice-under-field.jpg', type: 'jpeg', quality: 70 });
  if (puts.length > before) { await page.keyboard.press('Alt+KeyZ'); await page.waitForTimeout(800); log('  after Alt+Z shown', await phone.inputValue(), 'PUTs', JSON.stringify(puts.slice(before))); }
  else { await page.keyboard.press('Escape'); await page.waitForTimeout(300); const d = page.getByRole('dialog'); if (await d.count()) { log('  leave dialog:', (await d.textContent()).slice(0, 120)); await d.getByRole('button', { name: /Discard/ }).click(); } }
}
// Fujairah and other edge entries: what happens, saved or not, and with Enter
for (const entry of ['97123456', '97197123456', '9715055501', '97150555019', '971971234567']) {
  await page.locator('.record-header h2').click();
  await phone.click();
  const before = puts.length;
  await page.keyboard.type(entry);
  await page.waitForTimeout(700);
  const shown = await phone.inputValue();
  const autosaved = puts.length > before;
  await page.keyboard.press('Enter');
  await page.waitForTimeout(900);
  const err = await page.locator('[data-field="phone"]').textContent();
  log(`edge ${entry}: shown ${shown}; autosaved ${autosaved}; after Enter PUTs ${JSON.stringify(puts.slice(before))}; field text "${err.replace(/\s+/g, ' ').slice(0, 140)}"; stored-now ${await phone.inputValue()}`);
  // put back
  const now = await phone.inputValue();
  if (puts.length > before) { await page.locator('.record-header h2').click(); await phone.click(); await page.keyboard.press('Control+a'); await page.keyboard.type(original.replace(/\s/g, '')); await page.waitForTimeout(500); await page.keyboard.press('Enter'); await page.waitForTimeout(700); }
  else { await page.keyboard.press('Alt+KeyZ'); await page.waitForTimeout(400); }
  if (/Unsaved/.test(await page.locator('.record-header').textContent())) { await page.keyboard.press('Escape'); const d = page.getByRole('dialog'); if (await d.count()) await d.getByRole('button', { name: /Discard/ }).click(); }
}
log('phone now', await phone.inputValue(), '(original', original, ')');
// server validation
const email = page.locator('[data-field="email"] input');
await email.click(); await page.keyboard.press('Control+a'); await page.keyboard.type('not-an-email'); await page.keyboard.press('Control+s');
await page.waitForTimeout(900);
log('bad e-mail: field text', (await page.locator('[data-field="email"]').textContent()).replace(/\s+/g, ' '), 'focused', await email.evaluate(e => e === document.activeElement));
await page.screenshot({ path: OUT + '/02-server-error-on-field.jpg', type: 'jpeg', quality: 70 });
// leave warning: reload -> beforeunload
let beforeunload = false; page.on('dialog', async d => { beforeunload = d.type(); await d.dismiss(); });
await page.reload().catch(() => {}); await page.waitForTimeout(500);
log('reload while dirty raised:', beforeunload);
await page.keyboard.press('Escape'); await page.waitForTimeout(300);
log('Escape dialog buttons:', await page.getByRole('dialog').getByRole('button').allTextContents().catch(() => []));
await page.keyboard.press('Escape');
await page.keyboard.press('Alt+KeyZ'); await page.waitForTimeout(300);
log('after Alt+Z (discard): email', await email.inputValue(), 'unsaved', /Unsaved/.test(await page.locator('.record-header').textContent()));
const t1 = await page.locator('.record-header h2').textContent();
await page.keyboard.press('Alt+PageDown'); await page.waitForTimeout(700);
const t2 = await page.locator('.record-header h2').textContent();
await page.keyboard.press('Alt+PageUp'); await page.waitForTimeout(700);
log('next/prev:', t1, '->', t2, '->', await page.locator('.record-header h2').textContent(), await page.locator('.record-position').textContent().catch(() => ''));
log('PUT count total', puts.length);
await browser.close();
