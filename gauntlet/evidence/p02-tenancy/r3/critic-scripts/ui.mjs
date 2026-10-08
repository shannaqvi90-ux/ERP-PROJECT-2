// Critic p02 r3: UI walk in Chromium, English and Arabic, keyboard first. BASE=http://localhost:20250
import { chromium } from '/home/user/critic/pw/node_modules/playwright-core/index.mjs';
const BASE = process.env.BASE || 'http://localhost:20250';
const OUT = process.env.OUT || '/home/user/evidence-staging/p02-tenancy/r3';
const PW = 'Demo-Pass-2026';
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
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
const shot = (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 60 });
const step = async (label, fn) => { try { const r = await fn(); log('OK  ', label, r === undefined ? '' : JSON.stringify(r)); } catch (e) { log('FAIL', label, String(e.message).split('\n')[0]); } };

// 1. English administrator.
{
  const { page } = await session('admin@alnoor.example');
  await step('workplace chip text', async () => page.getByTestId('workplace').textContent());
  await step('html dir/lang', async () => page.evaluate(() => [document.documentElement.dir, document.documentElement.lang]));
  await page.goto(BASE + '/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  await step('companies rows', async () => page.locator('table[role=grid] tbody tr').count());
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.locator('.record-form').waitFor(); await page.waitForTimeout(600);
  await step('company form fields', async () => page.evaluate(() => [...document.querySelectorAll('.record-form [data-field]')].map(e => e.getAttribute('data-field'))));
  await step('company header', async () => page.locator('.record-header h2').textContent());
  await shot(page, '01-company-form-en');
  await page.goto(BASE + '/tenancy/branches'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  await step('branches rows / total text', async () => [await page.locator('table[role=grid] tbody tr').count(), await page.locator('main').innerText().then(t => (t.match(/\d+ branches/) || [''])[0])]);
  await page.goto(BASE + '/tenancy/access'); await page.waitForTimeout(1500);
  await step('access screen heading', async () => page.locator('main h1').textContent());
  await page.goto(BASE + '/tenancy/tenant'); await page.waitForTimeout(1200);
  await step('tenant settings fields', async () => page.evaluate(() => [...document.querySelectorAll('main [data-field]')].map(e => e.getAttribute('data-field'))));
  // Switcher by keyboard.
  await page.goto(BASE + '/'); await page.waitForTimeout(800);
  await page.keyboard.press('Alt+c'); await page.waitForTimeout(500);
  await step('switcher focused element', async () => page.evaluate(() => document.activeElement?.outerHTML.slice(0, 120)));
  await page.keyboard.type('shj'); await page.waitForTimeout(400);
  await shot(page, '02-switcher-en');
  await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
  await step('after Alt+C shj Enter', async () => page.getByTestId('workplace').textContent());
  await page.locator('.workplace-chip[data-company="ALN-DXB"]').click().catch(() => {}); await page.waitForTimeout(800);
  await step('after chip click', async () => page.getByTestId('workplace').textContent());
  await page.close();
}
// 2. Arabic administrator.
{
  const { page } = await session('admin.ar@alnoor.example', 'ar');
  await step('AR html dir/lang', async () => page.evaluate(() => [document.documentElement.dir, document.documentElement.lang]));
  await step('AR workplace chip', async () => page.getByTestId('workplace').textContent());
  await page.goto(BASE + '/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().waitFor(); await page.waitForTimeout(500);
  await step('AR companies first row', async () => page.locator('table[role=grid] tbody tr').first().innerText());
  await page.locator('table[role=grid] tbody tr').first().click(); await page.locator('.record-form').waitFor(); await page.waitForTimeout(700);
  await step('AR company header', async () => page.locator('.record-header h2').textContent());
  await step('AR labels', async () => page.evaluate(() => [...document.querySelectorAll('.record-form label')].slice(0, 12).map(l => l.textContent.trim())));
  await step('AR English-only text left on form', async () => page.evaluate(() => [...document.querySelectorAll('.record-form label, .record-form legend, .record-form button, .record-section h3, .record-section th')].map(e => e.textContent.trim()).filter(t => /[A-Za-z]{3,}/.test(t) && !/[؀-ۿ]/.test(t))));
  await shot(page, '03-company-form-ar');
  await page.keyboard.press('Escape'); await page.waitForTimeout(300);
  await page.keyboard.press('Alt+c'); await page.waitForTimeout(500);
  await shot(page, '04-switcher-ar');
  await page.keyboard.press('Escape');
  await page.goto(BASE + '/tenancy/branches'); await page.locator('table[role=grid] tbody tr').first().waitFor(); await page.waitForTimeout(500);
  await step('AR branches first row', async () => page.locator('table[role=grid] tbody tr').first().innerText());
  await page.goto(BASE + '/tenancy/access'); await page.waitForTimeout(1500);
  await shot(page, '05-access-ar');
  await page.close();
}
// 3. Read-only user.
{
  const { page } = await session('viewer@alnoor.example');
  await step('viewer chip tag/text', async () => page.getByTestId('workplace').evaluate(e => [e.tagName, e.textContent]));
  await page.goto(BASE + '/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().waitFor();
  await step('viewer New buttons', async () => page.getByRole('button', { name: 'New' }).count());
  await page.locator('table[role=grid] tbody tr').first().click(); await page.locator('.record-form').waitFor(); await page.waitForTimeout(700);
  await step('viewer Save buttons / logo buttons / quick-add', async () => [await page.getByRole('button', { name: 'Save' }).count(),
    await page.locator('.company-logo button, [class*=logo] button, input[type=file]').count(), await page.locator('form.quick-add').count()]);
  await page.goto(BASE + '/tenancy/branches'); await page.waitForTimeout(1200);
  await step('viewer branches New', async () => page.getByRole('button', { name: 'New' }).count());
  await page.goto(BASE + '/tenancy/access'); await page.waitForTimeout(1000);
  await step('viewer access screen', async () => page.locator('main').innerText().then(t => t.slice(0, 80)));
  await page.close();
}
await browser.close();
