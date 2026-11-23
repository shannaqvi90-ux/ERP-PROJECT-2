import { createRequire } from 'node:module';
const require = createRequire('/home/shan/critic/p09-custom-fields-r3/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
export const BASE = 'http://localhost:20950';
export const OUT = '/home/shan/evidence-staging/p09-custom-fields/r3/ui';
export async function open(email, { width = 1440, height = 900 } = {}) {
  const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome', headless: true });
  const page = await browser.newPage({ viewport: { width, height } });
  const errors = [];
  page.on('response', r => { if (r.status() >= 500) errors.push(r.status() + ' ' + r.url()); });
  page.on('pageerror', e => errors.push('pageerror ' + e.message));
  await page.goto(BASE + '/?domain=alnoor.example');
  await page.waitForTimeout(1500);
  await page.keyboard.type(email.split('@')[0]); await page.keyboard.press('Enter');
  await page.waitForTimeout(800);
  await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.waitForTimeout(3000);
  return { browser, page, errors };
}
export const shot = (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 60 });
export const focused = page => page.evaluate(() => { const e = document.activeElement; return e ? `${e.tagName} name=${e.getAttribute('name')} aria=${e.getAttribute('aria-label')} text=${(e.textContent||'').trim().slice(0,40)}` : 'none'; });
