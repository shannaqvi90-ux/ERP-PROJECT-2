import { chromium } from '/home/shan/critic/p03-identity-r12/gauntlet/compare/node_modules/playwright-core/index.mjs';
export const BASE = process.env.BASE || 'http://localhost:20350';
export const EV = '/home/shan/evidence-staging/p03-identity/r12';
export async function open({ width = 1600, height = 900, locale = 'en-US' } = {}) {
  const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
  const context = await browser.newContext({ viewport: { width, height }, locale });
  const page = await context.newPage();
  const errors = [];
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('response', (r) => { if (r.status() >= 500) errors.push(`${r.status()} ${r.url()}`); });
  return { browser, context, page, errors };
}
// keyboard sign-in: type e-mail, Tab, password, Enter
export async function signIn(page, email, password = 'Demo-Pass-2026') {
  await page.goto(BASE + '/');
  await page.locator('input[type=email], input[name=email]').first().waitFor();
  await page.locator('input[type=email], input[name=email]').first().focus();
  await page.keyboard.type(email);
  await page.keyboard.press('Tab');
  await page.keyboard.type(password);
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1800);
  if (await page.locator('input[type=password]').count()) console.log('STILL ON SIGN-IN: ' + (await page.locator('body').innerText()).slice(0, 300).replace(/\n/g, ' | '));
}
export const focused = (page) => page.evaluate(() => { const e = document.activeElement; return e ? `${e.tagName.toLowerCase()}${e.getAttribute('name') ? '[name=' + e.getAttribute('name') + ']' : ''}${e.getAttribute('aria-label') ? '[aria-label=' + e.getAttribute('aria-label') + ']' : ''} "${(e.innerText || e.value || '').slice(0, 40)}"` : 'none'; });
export const shot = (page, name) => page.screenshot({ path: `${EV}/${name}.jpg`, quality: 65, type: 'jpeg' });
