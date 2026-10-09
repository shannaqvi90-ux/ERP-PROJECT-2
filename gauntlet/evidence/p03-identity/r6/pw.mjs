import { chromium } from '/home/shan/critic/p03-identity-r6/gauntlet/compare/node_modules/playwright-core/index.mjs';
export const BASE = 'http://localhost:20350';
export const SHOTS = '/home/shan/evidence-staging/p03-identity/r6';
export async function open(width = 1600, height = 900) {
  const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome', headless: true });
  const page = await browser.newPage({ viewport: { width, height } });
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('response', r => { if (r.status() >= 500) errors.push(`${r.status()} ${r.url()}`); });
  return { browser, page, errors };
}
export async function signIn(page, email, pw = 'Demo-Pass-2026') {
  await page.goto(BASE + '/');
  await page.waitForLoadState('networkidle');
  const emailBox = page.locator('input[type=email], input[name=email], input[autocomplete=username]').first();
  await emailBox.fill(email);
  await page.keyboard.press('Enter');
  const pwBox = page.locator('input[type=password]').first();
  await pwBox.waitFor();
  await pwBox.fill(pw);
  await page.keyboard.press('Enter');
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(800);
}
export async function shot(page, name) { await page.screenshot({ path: `${SHOTS}/${name}.jpg`, type: 'jpeg', quality: 70 }); }
