import { createRequire } from 'node:module';
import fs from 'node:fs';
const require = createRequire('/home/shan/critic/p04-shell-r12/gauntlet/compare/package.json');
export const { chromium } = require('playwright-core');
export const BASE = process.env.BASE || 'http://localhost:20450';
export const OUT = '/home/shan/evidence-staging/p04-shell/r12';
export const exe = `${process.env.HOME}/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome`;
export async function launch() { return chromium.launch({ executablePath: exe }); }
export async function signIn(page, email, pw = 'Demo-Pass-2026') {
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]').waitFor();
  await page.locator('input[name="email"]').fill(email);
  await page.keyboard.press('Tab');
  await page.keyboard.type(pw);
  await page.keyboard.press('Enter');
  await page.locator('nav.navpane a').first().waitFor({ timeout: 30000 });
  await page.waitForTimeout(400);
}
export const shot = (page, name) => page.screenshot({ path: `${OUT}/${name}`, type: 'jpeg', quality: 70 });
export function save(name, obj) { fs.writeFileSync(`${OUT}/${name}`, JSON.stringify(obj, null, 2)); }
