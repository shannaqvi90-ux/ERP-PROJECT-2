// Shared helpers for the critic's browser checks (playwright-core from the compare harness, preinstalled Chromium).
import { chromium } from '/home/shan/critic/p05-list-search-r7-plant/gauntlet/compare/node_modules/playwright-core/index.mjs';
import fs from 'node:fs';
import path from 'node:path';
const exe = fs.readdirSync(path.join(process.env.HOME, '.cache/ms-playwright')).filter(d => d.startsWith('chromium-')).map(d => path.join(process.env.HOME, '.cache/ms-playwright', d, 'chrome-linux64/chrome')).find(f => fs.existsSync(f))
  || fs.readdirSync(path.join(process.env.HOME, '.cache/ms-playwright')).filter(d => d.startsWith('chromium-')).map(d => path.join(process.env.HOME, '.cache/ms-playwright', d, 'chrome-linux/chrome')).find(f => fs.existsSync(f));
export async function open(base, email, { width = 1440, height = 900, locale = 'en-US' } = {}) {
  const browser = await chromium.launch({ executablePath: exe });
  const context = await browser.newContext({ viewport: { width, height }, locale, timezoneId: 'Asia/Dubai' });
  const page = await context.newPage();
  await page.goto(base + '/');
  await page.getByLabel('E-mail', { exact: true }).or(page.locator('input[name="email"]')).first().fill(email);
  await page.getByLabel('Password', { exact: true }).or(page.locator('input[name="password"]')).first().fill('Demo-Pass-2026');
  await page.keyboard.press('Enter');
  await page.getByRole('navigation').first().waitFor();
  return { browser, context, page };
}
export const shot = (page, file) => page.screenshot({ path: file, type: 'jpeg', quality: 70 });
