// Critic p02 r4: UI walk in Chromium, English and Arabic, keyboard first. BASE=http://localhost:20250
import { chromium } from '/home/shan/critic/p02-tenancy-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = process.env.BASE || 'http://localhost:20250';
const OUT = process.env.OUT || '/home/shan/evidence-staging/p02-tenancy/r4';
const PW = 'Demo-Pass-2026';
const browser = await chromium.launch({ executablePath: process.env.CHROME });
const log = (...a) => console.log(...a);
async function session(email, lang = 'en', viewport = { width: 1440, height: 900 }) {
  const ctx = await browser.newContext({ viewport, locale: lang === 'ar' ? 'ar-AE' : 'en-AE' });
  const page = await ctx.newPage();
  await page.goto(BASE + '/');
  await page.evaluate(l => { localStorage.clear(); localStorage.setItem('erp.language', l); }, lang);
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type(PW); await page.keyboard.press('Enter');
  await page.locator('nav[aria-label]').first().waitFor();
  await page.waitForTimeout(800);
  return { ctx, page };
}
const shot = (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 55 });
const step = async (label, fn) => { try { const r = await fn(); log('OK  ', label, r === undefined ? '' : JSON.stringify(r)); } catch (e) { log('FAIL', label, String(e.message).split('\n')[0]); } };
const tag = Date.now() % 100000;

// 1. English administrator: keyboard-only company + branch creation, then the switcher.
{
  const { page } = await session('admin@alnoor.example');
  await step('html dir/lang', async () => page.evaluate(() => [document.documentElement.dir, document.documentElement.lang]));
  await step('workplace label', async () => page.getByTestId('workplace').textContent());
  await page.goto(BASE + '/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  await step('companies rows', async () => page.locator('table[role=grid] tbody tr').count());
  await step('Alt+N opens a new company with the English name focused', async () => {
    await page.keyboard.press('Alt+n'); await page.locator('[data-field="legalNameEn"] input:focus').waitFor({ timeout: 5000 });
    await page.keyboard.type(`Critic Keyboard ${tag} LLC`);
    await page.keyboard.press('Tab'); await page.keyboard.type(`الناقد ${tag} ذ.م.م`);
    await page.keyboard.press('Control+s');
    await page.locator('input[name="branchNameEn"]:focus').waitFor({ timeout: 8000 });
    const offered = await page.locator('input[name="branchNameEn"]').inputValue();
    await page.keyboard.type('Musaffah Workshop'); await page.keyboard.press('Enter');
    await page.waitForFunction(() => [...document.querySelectorAll('.record-section table tbody tr')].some(r => r.textContent.includes('Musaffah')), null, { timeout: 8000 });
    return { offered, rows: await page.locator('.record-section table tbody tr').allInnerTexts() };
  });
  await step('Arabic branch name required? (branch saved with no Arabic name)', async () => page.locator('.record-section table tbody tr').first().innerText());
  await page.waitForTimeout(500);
  await shot(page, '01-company-keyboard-en');
  await step('company form fields', async () => page.evaluate(() => [...document.querySelectorAll('.record-form [data-field]')].map(e => e.getAttribute('data-field'))));
  await step('Alt+C opens switcher, type filter, Enter', async () => {
    await page.keyboard.press('Escape');
    await page.locator('body').click({ position: { x: 5, y: 400 } }).catch(() => {});
    await page.keyboard.press('Alt+c'); await page.locator('.workplace-popover input:focus').waitFor({ timeout: 5000 });
    await page.keyboard.type('sharjah');
    const options = await page.locator('.workplace-popover [role=option]').allInnerTexts();
    await shot(page, '02-switcher-en');
    await page.keyboard.press('Enter'); await page.waitForTimeout(800);
    return { options: options.slice(0, 6), now: await page.getByTestId('workplace').textContent() };
  });
  await page.goto(BASE + '/tenancy/branches'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  await step('branches list total', async () => page.locator('main').innerText().then(t => (t.match(/\d+ branches/) || [''])[0]));
  await page.goto(BASE + '/tenancy/tenant'); await page.waitForTimeout(1200);
  await step('workspace settings fields', async () => page.evaluate(() => [...document.querySelectorAll('main [data-field], main select, main input')].map(e => e.getAttribute('data-field') || e.getAttribute('name'))));
  await shot(page, '03-workspace-en');
}

// 2. Arabic administrator: company form, switcher, access, RTL.
{
  const { page } = await session('admin.ar@alnoor.example', 'ar');
  await step('ar html dir/lang', async () => page.evaluate(() => [document.documentElement.dir, document.documentElement.lang]));
  await page.goto(BASE + '/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.locator('table[role=grid] tbody tr').first().click(); await page.locator('.record-form').waitFor(); await page.waitForTimeout(700);
  await step('ar company header', async () => page.locator('.record-form h2, .record-header h2').first().textContent());
  await step('ar latin-only visible texts in main (labels)', async () => page.evaluate(() => [...document.querySelectorAll('main label, main th, main legend, main h1, main h2, main h3, main button')].map(e => e.textContent.trim()).filter(t => /[A-Za-z]{3,}/.test(t) && !/[؀-ۿ]/.test(t)).slice(0, 20)));
  await shot(page, '04-company-form-ar');
  await step('ar switcher', async () => {
    await page.keyboard.press('Escape');
    await page.keyboard.press('Alt+c'); await page.locator('.workplace-popover input:focus').waitFor({ timeout: 5000 });
    const options = await page.locator('.workplace-popover [role=option]').allInnerTexts();
    await shot(page, '05-switcher-ar');
    await page.keyboard.press('Escape');
    return options.slice(0, 4);
  });
  await page.goto(BASE + '/tenancy/access'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.locator('table[role=grid] tbody tr').nth(2).click(); await page.waitForTimeout(1200);
  await step('ar access form', async () => page.locator('main').innerText().then(t => t.slice(0, 300)));
  await shot(page, '06-access-ar');
  await page.goto(BASE + '/tenancy/branches'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  await step('ar branches total', async () => page.locator('main').innerText().then(t => t.slice(0, 200)));
}

// 3. Viewer: read-only.
{
  const { page } = await session('viewer@alnoor.example');
  await page.goto(BASE + '/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.locator('table[role=grid] tbody tr').first().click(); await page.locator('.record-form').waitFor(); await page.waitForTimeout(700);
  await step('viewer controls offered', async () => page.evaluate(() => [...document.querySelectorAll('main button, main input[type=file], form.quick-add')].filter(e => !e.disabled && !e.closest('fieldset:disabled')).map(e => e.tagName + ':' + e.textContent.trim().slice(0, 20))));
  await step('viewer workplace is label', async () => page.getByTestId('workplace').evaluate(e => e.tagName));
}
await browser.close();
