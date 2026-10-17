import { chromium } from '/home/shan/critic/p06-form-report-r7/gauntlet/compare/node_modules/playwright-core/index.mjs';
export const BASE = process.env.OURS || 'http://localhost:20650';
export const OUT = '/home/shan/evidence-staging/p06-form-report/r7/browser';
export async function api(email, password = 'Demo-Pass-2026') {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password, issueToken: true }) });
  const j = await r.json(); if (!j.token) throw new Error('sign-in ' + r.status + JSON.stringify(j));
  const call = async (method, path, body, headers = {}) => {
    const res = await fetch(BASE + path, { method, headers: { Authorization: 'Bearer ' + j.token, 'X-Erp-Request': '1', ...(body ? { 'Content-Type': 'application/json' } : {}), ...headers }, body: body ? JSON.stringify(body) : undefined });
    const ct = res.headers.get('content-type') || '';
    return { status: res.status, ct, body: ct.includes('json') ? await res.json() : Buffer.from(await res.arrayBuffer()) };
  };
  return { token: j.token, call };
}
export async function browser() { return chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' }); }
export async function signIn(b, email, opts = {}) {
  const ctx = await b.newContext({ viewport: { width: 1400, height: 900 }, acceptDownloads: true, ...opts });
  const page = await ctx.newPage();
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]').waitFor();
  await page.locator('input[name="email"]').fill(email);
  await page.keyboard.press('Tab');
  await page.keyboard.type('Demo-Pass-2026');
  await page.keyboard.press('Enter');
  await page.locator('nav[aria-label="Main navigation"], nav[aria-label="التنقل الرئيسي"], nav').first().waitFor(); await page.waitForTimeout(500);
  return { ctx, page };
}
export const log = (...a) => console.log(...a);
