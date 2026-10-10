// Critic p06 r5: the form and report framework used as a person would, in English and Arabic, by keyboard.
import { chromium } from '/home/shan/critic/p06-form-report-r5/gauntlet/compare/node_modules/playwright-core/index.mjs';
import { writeFileSync } from 'node:fs';
const base = process.argv[2];
const R = '/home/shan/evidence-staging/p06-form-report/r5';
const log = []; const L = (...a) => { const s = a.join(' '); log.push(s); console.log(s); };
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
async function session(email) {
  const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 }, acceptDownloads: true });
  const page = await ctx.newPage();
  const writes = [];
  page.on('request', r => { if (r.method() !== 'GET' && r.url().includes('/api/') && !r.url().includes('/auth/')) writes.push(`${r.method()} ${new URL(r.url()).pathname}`); });
  await page.goto(base + '/');
  await page.locator('input[name="email"]').fill(email);
  await page.locator('input[name="password"]').fill('Demo-Pass-2026');
  await page.keyboard.press('Enter');
  await page.locator('nav[aria-label]').first().waitFor();
  return { ctx, page, writes };
}
const shot = (page, name) => page.screenshot({ path: `${R}/${name}.jpg`, type: 'jpeg', quality: 60 });
const active = page => page.evaluate(() => { const e = document.activeElement; return `${e?.tagName} ${e?.getAttribute('name') || e?.getAttribute('aria-label') || e?.textContent?.trim().slice(0, 30)}`; });
const wait = ms => new Promise(r => setTimeout(r, ms));

// 1. English admin: companies list -> open by keyboard; edit; Enter saves; leave guard; server error; discard; next/previous.
{
  const { ctx, page, writes } = await session('admin@alnoor.example');
  await page.goto(base + '/tenancy/companies');
  await page.locator('[role=grid], table').first().waitFor();
  await wait(1000);
  L('companies list focus on arrival:', await active(page));
  await page.keyboard.press('ArrowDown'); await wait(200);
  await page.keyboard.press('Enter'); await wait(1200);
  L('ArrowDown+Enter on list -> url', page.url());
  if (!/companies\/[0-9a-f-]{36}/.test(page.url())) {
    await page.locator('[role=grid], table').first().focus().catch(() => {});
    await page.keyboard.press('Enter'); await wait(1000);
    L('focus grid + Enter -> url', page.url());
  }
  if (!/companies\/[0-9a-f-]{36}/.test(page.url())) {
    await page.locator('[role=row] >> nth=1').dblclick().catch(() => {}); await wait(1000);
    L('dblclick -> url', page.url());
  }
  const phone = page.locator('[data-field="phone"] input');
  await phone.waitFor();
  const before = await phone.inputValue();
  L('form opened, phone', before, 'focus', await active(page));
  const sections = await page.locator('.record-form fieldset legend, .record-form h2, .record-form [role=tab]').allTextContents();
  L('sections/tabs:', JSON.stringify(sections));
  const u0 = page.url();
  await page.keyboard.press('Alt+PageDown'); await wait(1000);
  L('Alt+PageDown moved:', page.url() !== u0);
  await page.keyboard.press('Alt+PageUp'); await wait(1000);
  L('Alt+PageUp back:', page.url() === u0);
  // Enter in a one-line field saves
  await phone.click(); await page.keyboard.type('+971 4 555 0123'); await page.keyboard.press('Enter'); await wait(1500);
  L('Enter in phone saves -> notice:', (await page.locator('.record-form .notice').textContent().catch(() => '')).slice(0, 80), 'value', await phone.inputValue());
  // dirty + Escape -> guard
  await phone.click(); await page.keyboard.type('9');
  L('dirty header:', (await page.locator('.record-header').textContent())?.replace(/\s+/g, ' ').slice(0, 120));
  await page.keyboard.press('Escape'); await wait(400);
  L('Escape while dirty dialog:', (await page.locator('[role=dialog], [role=alertdialog]').first().textContent().catch(() => 'none'))?.replace(/\s+/g, ' ').slice(0, 200));
  await shot(page, '01-form-en-leave-guard');
  await page.keyboard.press('Escape'); await wait(300);
  await page.keyboard.press('Alt+z'); await wait(300);
  L('after Alt+Z phone', await phone.inputValue());
  // browser-level leave (reload) while dirty
  await phone.click(); await page.keyboard.type('7');
  let unloadDialog = 'none';
  page.once('dialog', d => { unloadDialog = d.type(); d.dismiss(); });
  await page.reload({ timeout: 3000 }).catch(() => {}); await wait(500);
  L('reload while dirty -> browser dialog:', unloadDialog);
  await page.goto(u0); await phone.waitFor();
  // server validation: bad email, then multiline field Enter does not save
  const email = page.locator('[data-field="email"] input');
  const oldEmail = await email.inputValue();
  await email.fill('not-an-email');
  await page.keyboard.press('Control+s'); await wait(1200);
  L('Ctrl+S with bad e-mail -> errors:', JSON.stringify(await page.locator('.field-error, [role=alert], .form-error').allTextContents()).slice(0, 300), 'focus', await active(page));
  await shot(page, '02-form-en-server-error');
  await email.fill(oldEmail); await page.keyboard.press('Control+Enter'); await wait(1200);
  const ta = page.locator('.record-form textarea').first();
  if (await ta.count()) { const v = await ta.inputValue(); await ta.click(); await page.keyboard.press('End'); await page.keyboard.press('Enter'); await wait(500); L('Enter in multiline -> newline kept, saved?', (await ta.inputValue()).length === v.length + 1, 'notice', (await page.locator('.record-form .notice').textContent().catch(() => '')).slice(0, 60)); await page.keyboard.press('Alt+z'); }
  // restore phone
  await phone.click(); await page.keyboard.type(before); await page.keyboard.press('Enter'); await wait(1200);
  L('restored phone', await phone.inputValue());
  // print menu
  await page.keyboard.press('Alt+r'); await wait(400);
  L('Alt+R print menu (en):', JSON.stringify(await page.locator('[role=menu] [role=menuitem]').allTextContents()), 'focus', await active(page));
  await page.keyboard.press('Escape');
  L('admin writes:', writes.join(', '));
  await ctx.close();
}
// 2. Viewer: read-only form, keys send nothing.
{
  const { ctx, page, writes } = await session('viewer@alnoor.example');
  await page.goto(base + '/tenancy/companies'); await wait(1500);
  const link = await page.evaluate(() => [...document.querySelectorAll('a')].map(a => a.getAttribute('href')).find(h => /companies\/[0-9a-f-]{36}/.test(h || '')));
  if (link) await page.goto(base + link); else { await page.locator('[role=row] >> nth=1').dblclick().catch(() => {}); }
  await wait(1500);
  const disabled = await page.evaluate(() => { const f = [...document.querySelectorAll('.record-form input, .record-form select, .record-form textarea')]; return `${f.filter(x => x.disabled || x.readOnly || x.closest('fieldset:disabled')).length}/${f.length}`; });
  await page.locator('[data-field="phone"] input').click().catch(() => {});
  for (const k of ['Enter', 'Control+s', 'Control+Enter', 'Alt+z', 'Delete', 'Alt+n']) { await page.keyboard.press(k); await wait(200); }
  L('viewer form url', page.url(), 'disabled', disabled, 'save buttons', await page.locator('.record-form button[type=submit]').count(), 'reason', (await page.locator('.record-form').textContent())?.match(/read[- ]only[^.]*\./i)?.[0], 'writes', JSON.stringify(writes));
  await shot(page, '03-form-en-viewer-readonly');
  await ctx.close();
}
// 3. Arabic admin: form RTL, print menu by keyboard, Arabic PDF; reports screen; list print menu.
{
  const { ctx, page } = await session('admin.ar@alnoor.example');
  await page.goto(base + '/tenancy/companies'); await wait(1500);
  const link = await page.evaluate(() => [...document.querySelectorAll('a')].map(a => a.getAttribute('href')).find(h => /companies\/[0-9a-f-]{36}/.test(h || '')));
  if (link) await page.goto(base + link); else { await page.locator('[role=row] >> nth=1').dblclick(); }
  await page.locator('[data-field="phone"] input').waitFor();
  L('ar dir', await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang));
  await page.locator('[data-field="phone"] input').click(); await page.keyboard.type('5');
  await shot(page, '04-form-ar-rtl-dirty');
  await page.keyboard.press('Escape'); await wait(300);
  L('ar guard dialog:', (await page.locator('[role=dialog], [role=alertdialog]').first().textContent().catch(() => 'none'))?.replace(/\s+/g, ' ').slice(0, 160));
  await page.keyboard.press('Escape'); await page.keyboard.press('Alt+z'); await wait(300);
  await page.keyboard.press('Alt+r'); await wait(400);
  L('Alt+R print menu (ar):', JSON.stringify(await page.locator('[role=menu] [role=menuitem]').allTextContents()), 'focus', await active(page));
  await shot(page, '05-form-ar-print-menu');
  const dl = page.waitForEvent('download', { timeout: 20000 }).catch(() => null);
  const popup = ctx.waitForEvent('page', { timeout: 20000 }).catch(() => null);
  await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
  const d = await Promise.race([dl, popup]);
  if (d && d.suggestedFilename) { await d.saveAs(`${R}/browser/company-profile-ar.pdf`); L('downloaded', d.suggestedFilename()); }
  else if (d) { L('popup', d.url()); }
  else L('no download or popup from the print menu');
  // list print menu on users list in Arabic
  await page.goto(base + '/identity/users'); await wait(2000);
  await page.keyboard.press('Alt+Shift+r'); await wait(500);
  L('list Alt+Shift+R menu (ar):', JSON.stringify(await page.locator('[role=menu] [role=menuitem], [role=dialog] button').allTextContents()).slice(0, 400), 'focus', await active(page));
  await shot(page, '06-list-print-menu-ar');
  await page.keyboard.press('Escape');
  // reports screen
  await page.goto(base + '/reports/catalog'); await wait(2000);
  L('reports screen entries:', JSON.stringify((await page.locator('main a, main button').allTextContents()).slice(0, 30)));
  await shot(page, '07-reports-catalog-ar');
  await ctx.close();
}
writeFileSync(`${R}/browser/explore.log`, log.join('\n'));
await browser.close();
