import { createRequire } from 'node:module';
const require = createRequire('/home/shan/critic/p05-list-search-r15/gauntlet/compare/package.json');
export const { chromium } = require('playwright-core');
export const PW = process.env.ERP_DEMO_PASSWORD || 'Demo-Pass-2026';
export async function launch() { return chromium.launch({ headless: true, executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' }); }
export async function signIn(browser, base, email, { lang = 'en', width = 1600, height = 900 } = {}) {
  const context = await browser.newContext({ viewport: { width, height }, locale: lang === 'ar' ? 'ar-AE' : 'en-US' });
  const page = await context.newPage();
  await page.goto(base + '/');
  await page.locator('input[name="email"], input[type="email"]').first().fill(email);
  await page.locator('input[type="password"]').first().fill(PW);
  await page.keyboard.press('Enter');
  await page.getByRole('navigation').first().waitFor();
  return { context, page };
}
export async function api(base, email) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW }) });
  const cookie = (r.headers.getSetCookie?.() || []).map(c => c.split(';')[0]).join('; ');
  const call = async (method, path, body) => { const x = await fetch(base + path, { method, headers: { cookie, 'X-Erp-Request': '1', 'content-type': 'application/json' }, body: body ? JSON.stringify(body) : undefined }); const t = await x.text(); let j; try { j = JSON.parse(t); } catch { j = t; } return { status: x.status, body: j }; };
  return { get: p => call('GET', p), post: (p, b) => call('POST', p, b), put: (p, b) => call('PUT', p, b), del: p => call('DELETE', p) };
}
