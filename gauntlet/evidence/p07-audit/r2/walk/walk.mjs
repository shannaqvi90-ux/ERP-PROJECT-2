// Critic walk of p07 (audit trail) in a real Chromium: English and Arabic, by keyboard.
import { chromium } from '/home/shan/critic/p07-audit-r2/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = process.env.BASE || 'http://localhost:20750';
const OUT = '/home/shan/evidence-staging/p07-audit/r2';
const exe = `${process.env.HOME}/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome`;
const browser = await chromium.launch({ executablePath: exe });
const log = (...a) => console.log(...a);
async function session(email, lang) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: lang === 'ar' ? 'ar-AE' : 'en-US' });
  const page = await ctx.newPage();
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]').waitFor();
  await page.locator('input[name="email"]').fill(email);
  await page.keyboard.press('Tab');
  await page.keyboard.type('Demo-Pass-2026');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(2500);
  return { ctx, page };
}
const shot = (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 60 });
const step = process.argv[2] || 'en';
if (step === 'en') {
  const { page } = await session('admin@alnoor.example', 'en');
  // Keyboard: G then R opens Roles
  await page.keyboard.press('Escape');
  await page.keyboard.press('g'); await page.keyboard.press('r');
  await page.waitForTimeout(2000);
  log('url after G R', page.url());
  const rows = page.locator('table[role=grid] tbody tr');
  await rows.first().waitFor();
  await rows.filter({ hasText: 'Staff' }).first().click();
  await page.waitForTimeout(1500);
  const field = page.locator('[data-field="nameEn"] input');
  const old = await field.inputValue();
  await field.fill(old + ' X');
  await field.press('Control+KeyS');
  await page.waitForTimeout(2000);
  log('latest line:', await page.getByTestId('audit-latest').innerText().catch(e => 'NONE ' + e.message));
  await page.keyboard.press('Alt+KeyY');
  await page.waitForTimeout(1500);
  const hist = page.getByTestId('audit-history');
  log('history visible', await hist.isVisible());
  log('history text head:', (await hist.innerText().catch(() => '')).slice(0, 600));
  await shot(page, '01-role-history-en');
  await page.keyboard.press('Escape');
  await field.fill(old); await field.press('Control+KeyS'); await page.waitForTimeout(1500);
  // Audit log by URL and by menu
  await page.goto(BASE + '/audit/log');
  await rows.first().waitFor({ timeout: 30000 });
  await page.waitForTimeout(1500);
  log('log header/total:', (await page.locator('main').innerText()).slice(0, 500).replace(/\n/g, ' | '));
  await shot(page, '02-audit-log-en');
  // Search by record name
  await page.keyboard.type('staff');
  await page.waitForTimeout(2500);
  log('rows after search staff:', await rows.count());
  log('first row:', (await rows.first().innerText()).replace(/\n/g, ' | ').slice(0, 300));
  await rows.first().click();
  await page.waitForTimeout(1500);
  log('change panel:', (await page.locator('.audit-change').innerText().catch(() => 'none')).slice(0, 500).replace(/\n/g, ' | '));
  await shot(page, '03-audit-log-change-en');
  // Filter controls: list visible filter buttons
  const btns = await page.locator('main button').allInnerTexts();
  log('buttons:', btns.filter(Boolean).slice(0, 40).join(' / '));
}
if (step === 'ar') {
  const { page } = await session('admin.ar@alnoor.example', 'ar');
  log('dir', await page.locator('html').getAttribute('dir'), 'lang', await page.locator('html').getAttribute('lang'));
  await page.goto(BASE + '/audit/log');
  const rows = page.locator('table[role=grid] tbody tr');
  await rows.first().waitFor({ timeout: 30000 });
  await page.waitForTimeout(1500);
  log('ar log:', (await page.locator('main').innerText()).slice(0, 600).replace(/\n/g, ' | '));
  await shot(page, '04-audit-log-ar');
  await rows.first().click();
  await page.waitForTimeout(1500);
  log('ar change:', (await page.locator('.audit-change').innerText().catch(() => 'none')).slice(0, 500).replace(/\n/g, ' | '));
  await shot(page, '05-audit-change-ar');
  await page.goto(BASE + '/identity/roles');
  await rows.first().waitFor();
  await rows.nth(1).click();
  await page.waitForTimeout(1500);
  await page.keyboard.press('Alt+KeyY');
  await page.waitForTimeout(1500);
  log('ar history:', (await page.getByTestId('audit-history').innerText().catch(() => 'none')).slice(0, 500).replace(/\n/g, ' | '));
  await shot(page, '06-role-history-ar');
}
await browser.close();
