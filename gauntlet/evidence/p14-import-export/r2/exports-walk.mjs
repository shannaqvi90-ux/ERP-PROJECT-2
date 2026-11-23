// Critic's walk of the full export screen, p14 round 2, by keyboard: g e opens it, Alt+N starts an export, the row is watched until ready, Download saves the zip.
// Run from gauntlet/compare of the critic clone: BASE=http://localhost:21450 OUT=<staging> LANG_RUN=en|ar node <staging>/exports-walk.mjs
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
const require = createRequire(path.join(process.cwd(), 'package.json'));
const { chromium } = require('playwright-core');
const { findChromium } = await import(path.join(process.cwd(), 'lib/browser.mjs'));
const BASE = process.env.BASE || 'http://localhost:21450';
const OUT = process.env.OUT || '.';
const L = process.env.LANG_RUN || 'en';
const email = L === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example';
const log = (...a) => console.log(`[${L}]`, ...a);
const shot = (page, name) => page.screenshot({ path: path.join(OUT, name), type: 'jpeg', quality: 60 });
const browser = await chromium.launch({ executablePath: findChromium(), headless: true });
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, acceptDownloads: true });
const page = await ctx.newPage();
page.on('response', r => { if (r.status() >= 500) log('5xx', r.status(), r.url()); });
page.on('pageerror', e => log('pageerror', e.message));
await page.goto(BASE + '/');
await page.locator('input[name="email"]:focus').waitFor();
await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.locator('nav').first().waitFor();
await page.waitForTimeout(800);
await page.keyboard.press('g'); await page.keyboard.press('e');
await page.locator('[data-testid="export-start"]').waitFor({ timeout: 15000 });
log('url', page.url(), 'dir', await page.evaluate(() => document.documentElement.dir));
const t0 = Date.now();
await page.keyboard.press('Alt+n');
const seen = new Set();
let ready = false;
for (let i = 0; i < 240; i++) {
  const first = page.locator('[data-testid="exports-table"] tbody tr').first();
  const txt = (await first.innerText().catch(() => '')).replace(/\s+/g, ' ');
  if (!seen.has(txt)) { seen.add(txt); log(`+${((Date.now() - t0) / 1000).toFixed(1)}s first row:`, txt); if (seen.size === 2) await shot(page, `${L === 'ar' ? '06-ar' : '05-en'}-export-running.jpg`); }
  if ((await first.getAttribute('data-status').catch(() => '')) === 'succeeded' && Date.now() - t0 > 1500) { ready = true; break; }
  if ((await first.getAttribute('data-status').catch(() => '')) === 'failed') break;
  await page.waitForTimeout(500);
}
log('ready', ready, 'after', ((Date.now() - t0) / 1000).toFixed(1), 's');
await shot(page, `${L === 'ar' ? '06b-ar' : '05b-en'}-export-ready.jpg`);
log('page text', (await page.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 700));
log('native selects on page', await page.locator('main select').count(), 'command bar', await page.locator('[role="toolbar"], .command-bar, [data-testid*="command"]').count());
if (ready) {
  const dl = page.waitForEvent('download', { timeout: 120000 });
  await page.locator('[data-testid="export-download"]').first().focus();
  await page.keyboard.press('Enter');
  const d = await dl; const p = path.join(OUT, `ui-${L}-export.zip`); await d.saveAs(p);
  log('downloaded', d.suggestedFilename(), fs.statSync(p).size, 'bytes');
  fs.unlinkSync(p);
}
await browser.close();
