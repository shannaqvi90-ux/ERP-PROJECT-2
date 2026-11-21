// Critic's browser walk, p14 round 1 (keyboard first). Run from gauntlet/compare of the critic clone:
//   BASE=http://localhost:21450 OUT=<staging> STEP=<name> node <staging>/ui-walk.mjs
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
const require = createRequire(path.join(process.cwd(), 'package.json'));
const { chromium } = require('playwright-core');
const { findChromium } = await import(path.join(process.cwd(), 'lib/browser.mjs')).catch(() => ({}));

const BASE = process.env.BASE || 'http://localhost:21450';
const OUT = process.env.OUT || '.';
const STEP = process.env.STEP || 'all';
const PW = 'Demo-Pass-2026';
const exe = findChromium ? findChromium() : undefined;
const browser = await chromium.launch({ executablePath: exe, headless: true });
const log = (...a) => console.log(...a);

async function session(email) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, acceptDownloads: true });
  const page = await ctx.newPage();
  page.on('response', r => { if (r.status() >= 500) log('5xx', r.status(), r.url()); });
  page.on('pageerror', e => log('pageerror', e.message));
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email);
  await page.keyboard.press('Tab');
  await page.keyboard.type(PW);
  await page.keyboard.press('Enter');
  await page.locator('nav').first().waitFor();
  await page.waitForTimeout(800);
  return { ctx, page };
}
const shot = (page, name) => page.screenshot({ path: path.join(OUT, name), type: 'jpeg', quality: 60 });

async function exportsPage(email, prefix) {
  const { ctx, page } = await session(email);
  await page.keyboard.press('g'); await page.keyboard.press('e');
  await page.locator('[data-testid="exports-table"], [data-testid="export-start"]').first().waitFor({ timeout: 15000 });
  log(prefix, 'exports url', page.url());
  await page.keyboard.press('Alt+n');
  await page.waitForTimeout(20000);
  log(prefix, 'exports text', (await page.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 900));
  await shot(page, `${prefix}-full-export.jpg`);
  await ctx.close();
}

if (STEP === 'exports-en' || STEP === 'all') await exportsPage('admin@alnoor.example', process.env.PREFIX || '06-en');
if (STEP === 'exports-ar') await exportsPage('admin.ar@alnoor.example', process.env.PREFIX || '07-ar');
await browser.close();
