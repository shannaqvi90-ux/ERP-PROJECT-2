// Critic p06 r4: the form and report framework used as a person would, in English and Arabic, by keyboard.
import { chromium } from '/home/shan/critic/p06-form-report-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
import { writeFileSync } from 'node:fs';
const base = process.argv[2];
const out = '/home/shan/evidence-staging/p06-form-report/r4/browser';
const shots = '/home/shan/evidence-staging/p06-form-report/r4';
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
const shot = (page, name) => page.screenshot({ path: `${shots}/${name}.jpg`, type: 'jpeg', quality: 70 });
const active = page => page.evaluate(() => { const e = document.activeElement; return `${e?.tagName} ${e?.getAttribute('name') || e?.getAttribute('aria-label') || e?.textContent?.slice(0, 30)}`; });

if (!process.env.ONLY3) {
// 1. English admin: companies list -> open a company by keyboard, edit, dirty, leave guard, save by keyboard, next record.
{
  const { ctx, page, writes } = await session('admin@alnoor.example');
  await page.goto(base + '/tenancy/companies');
  await page.locator('[role=grid], table').first().waitFor();
  await page.waitForTimeout(800);
  await page.keyboard.press('Enter').catch(() => {});
  await page.waitForTimeout(800);
  L('after Enter on list, url', page.url(), 'focus', await active(page));
  if (!/companies\/[0-9a-f-]{36}/.test(page.url())) {
    await page.locator('[role=row] >> nth=1').dblclick().catch(() => {});
    await page.waitForTimeout(800);
    L('after dblclick, url', page.url());
  }
  const phone = page.locator('[data-field="phone"] input');
  await phone.waitFor();
  const before = await phone.inputValue();
  L('company form url', page.url(), 'phone', before);
  // Alt+PageDown right after opening (focus where it lands)
  const u0 = page.url();
  await page.keyboard.press('Alt+PageDown'); await page.waitForTimeout(800);
  L('Alt+PageDown right after open: moved =', page.url() !== u0, 'focus', await active(page));
  if (page.url() !== u0) { await page.keyboard.press('Alt+PageUp'); await page.waitForTimeout(800); L('Alt+PageUp back:', page.url() === u0); }
  await phone.click(); await page.keyboard.type('+971 4 555 0123');
  const header = await page.locator('.record-header').textContent();
  L('dirty header text:', header?.replace(/\s+/g, ' ').slice(0, 120));
  await page.keyboard.press('Escape'); await page.waitForTimeout(400);
  const dialog = await page.locator('[role=dialog], [role=alertdialog]').first().textContent().catch(() => null);
  L('Escape while dirty dialog:', dialog?.replace(/\s+/g, ' ').slice(0, 200));
  await shot(page, '01-form-en-leave-guard');
  await page.keyboard.press('Escape'); await page.waitForTimeout(300);
  // server validation: bad email
  const email = page.locator('[data-field="email"] input');
  const oldEmail = await email.inputValue();
  await email.fill('not-an-email');
  await page.keyboard.press('Control+s'); await page.waitForTimeout(1000);
  const errs = await page.locator('.field-error, [role=alert], .form-error').allTextContents();
  L('save with bad e-mail -> errors:', JSON.stringify(errs).slice(0, 300), 'focus', await active(page));
  await shot(page, '02-form-en-server-error');
  await email.fill(oldEmail);
  await page.keyboard.press('Control+s'); await page.waitForTimeout(1200);
  L('after Ctrl+S notice:', (await page.locator('.record-form .notice').textContent().catch(() => ''))?.slice(0, 100), 'phone now', await phone.inputValue());
  // discard by keyboard
  await phone.click(); await page.keyboard.type('9');
  await page.keyboard.press('Alt+z'); await page.waitForTimeout(400);
  L('after Alt+Z phone', await phone.inputValue());
  // restore phone
  await phone.click(); await page.keyboard.type(before); await page.keyboard.press('Control+Enter'); await page.waitForTimeout(1000);
  L('restored phone', await phone.inputValue());
  // browser leave (menu link while dirty)
  await phone.click(); await page.keyboard.type('1');
  await page.locator('nav[aria-label] a').nth(1).click(); await page.waitForTimeout(500);
  L('menu link while dirty dialog:', (await page.locator('[role=dialog], [role=alertdialog]').first().textContent().catch(() => 'none'))?.replace(/\s+/g, ' ').slice(0, 120));
  await page.keyboard.press('Escape');
  L('admin writes:', writes.join(', '));
  await ctx.close();
}
// 2. Viewer: read-only form, keys send nothing.
{
  const { ctx, page, writes } = await session('viewer@alnoor.example');
  await page.goto(base + '/tenancy/companies');
  await page.waitForTimeout(1200);
  const link = await page.evaluate(() => [...document.querySelectorAll('a')].map(a => a.getAttribute('href')).find(h => /companies\/[0-9a-f-]{36}/.test(h || '')));
  if (link) await page.goto(base + link); else { await page.keyboard.press('Enter'); }
  await page.waitForTimeout(1200);
  const disabled = await page.evaluate(() => { const f = [...document.querySelectorAll('.record-form input, .record-form select, .record-form textarea')]; return `${f.filter(x => x.disabled || x.readOnly || x.closest('fieldset:disabled')).length}/${f.length}`; });
  for (const k of ['Control+s', 'Control+Enter', 'Alt+z', 'Delete', 'Alt+n']) { await page.keyboard.press(k); await page.waitForTimeout(200); }
  L('viewer form url', page.url(), 'disabled', disabled, 'save button', await page.locator('.record-form button[type=submit]').count(), 'writes', JSON.stringify(writes));
  await ctx.close();
}
}
// 3. Arabic admin: form RTL, print menu by keyboard, Arabic PDF; reports screen; list print menu.
{
  const { ctx, page, writes } = await session('admin.ar@alnoor.example');
  await page.goto(base + '/tenancy/companies');
  await page.locator('a[href*="/tenancy/companies/"]').first().waitFor({ timeout: 15000 }).catch(() => {});
  const link = await page.evaluate(() => [...document.querySelectorAll('a')].map(a => a.getAttribute('href')).find(h => /companies\/[0-9a-f-]{36}/.test(h || '')));
  L('ar company link', link);
  await page.goto(base + (link || '/tenancy/companies/018dca08-f08c-74c5-99f0-17c9cfe4b0db'));
  await page.locator('[data-field="phone"] input').waitFor();
  L('ar dir', await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang));
  await page.locator('[data-field="phone"] input').click(); await page.keyboard.type('5');
  await shot(page, '03-form-ar-rtl-dirty');
  await page.keyboard.press('Alt+z');
  await page.keyboard.press('Alt+r'); await page.waitForTimeout(400);
  const items = await page.locator('[role=menu] [role=menuitem]').allTextContents();
  L('Alt+R print menu (ar):', JSON.stringify(items), 'focus', await active(page));
  await shot(page, '04-form-ar-print-menu');
  const dl = page.waitForEvent('download', { timeout: 15000 }).catch(() => null);
  const popup = ctx.waitForEvent('page', { timeout: 15000 }).catch(() => null);
  await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
  const d = await Promise.race([dl, popup]);
  if (d && d.suggestedFilename) { await d.saveAs(`${out}/company-profile-ar.pdf`); L('downloaded', d.suggestedFilename()); }
  else if (d) { L('popup', d.url()); }
  // reports screen: role summary, Arabic
  await page.goto(base + '/reports/catalog');
  await page.waitForTimeout(1500);
  const reports = await page.locator('main a, main button').allTextContents();
  L('reports screen entries:', JSON.stringify(reports.slice(0, 30)));
  await ctx.close();
}
writeFileSync(`${out}/explore.log`, log.join('\n'));
await browser.close();
