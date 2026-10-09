import { chromium } from 'playwright-core';
export async function browser() { return chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' }); }
export async function signedIn(b, email, password = 'Demo-Pass-2026', vp = { width: 1400, height: 900 }) {
  const ctx = await b.newContext({ viewport: vp }); const p = await ctx.newPage();
  await p.goto('http://localhost:20250/');
  await p.locator('input[type=email]').fill(email);
  await p.locator('input[type=password]').fill(password);
  await p.locator('input[type=password]').press('Enter');
  await p.waitForLoadState('networkidle'); await p.waitForTimeout(800);
  return p;
}
