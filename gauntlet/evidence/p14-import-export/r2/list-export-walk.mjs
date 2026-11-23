// Critic p14 r2: list export with the list's current filter and columns, in English and Arabic: Exchange rates and Contacts lists,
// a column hidden, a filter applied, then Print or export > each menu item; reads the files.
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
const require = createRequire(path.join(process.cwd(), 'package.json'));
const { chromium } = require('playwright-core');
const { findChromium } = await import(path.join(process.cwd(), 'lib/browser.mjs'));
const { readFirstSheet } = await import(path.join(process.cwd(), 'lib/xlsx.mjs'));
const BASE = process.env.BASE || 'http://localhost:21450';
const OUT = process.env.OUT || '.';
const L = process.env.LANG_RUN || 'en';
const email = L === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example';
const log = (...a) => console.log(`[${L}]`, ...a);
const browser = await chromium.launch({ executablePath: findChromium(), headless: true });
const ctx = await browser.newContext({ viewport: { width: 1600, height: 900 }, acceptDownloads: true });
const page = await ctx.newPage();
page.on('response', r => { if (r.status() >= 500) log('5xx', r.status(), r.url()); });
page.on('pageerror', e => log('pageerror', e.message));
await page.goto(BASE + '/');
await page.locator('input[name="email"]:focus').waitFor();
await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.locator('nav').first().waitFor();
await page.goto(BASE + '/contacts/contacts?q=' + encodeURIComponent(''));
await page.locator('table[role=grid] tbody tr').first().waitFor(); await page.waitForTimeout(800);
// Filter: Emirate column options > Filter... > is > Dubai (custom field column also checked in the header).
const emirateLabel = L === 'ar' ? 'الإمارة' : 'Emirate';
const opt = page.locator('table[role=grid] thead th').filter({ hasText: emirateLabel }).locator('button').last();
await opt.click(); await page.getByRole('menuitem').first().click(); await page.waitForTimeout(500);
const dlg = page.locator('.list-filter');
await dlg.locator('select').first().selectOption({ index: 1 }).catch(() => {});
await dlg.locator('input:not([type=checkbox])').first().fill(L === 'ar' ? 'دبي' : 'Dubai').catch(async () => {});
await dlg.locator('button[type=submit]').click(); await page.waitForTimeout(2500);
const rowcount = await page.locator('table[role=grid]').getAttribute('aria-rowcount');
log('filtered rowcount', rowcount, 'url', page.url());
await page.screenshot({ path: path.join(OUT, `list-export-${L}-filtered.jpg`), type: 'jpeg', quality: 55 });
const headers = await page.locator('table[role=grid] thead th').allInnerTexts();
log('visible headers', headers.map(h => h.replace(/[▲▼▾\s]+/g, ' ').trim()).filter(Boolean).join(' | '));
await page.getByRole('button', { name: L === 'ar' ? /طباعة|تصدير/ : 'Print or export' }).click();
const items = await page.getByRole('menuitem').allInnerTexts();
log('menu', items.join(' | '));
for (const name of items.filter(i => /CSV|Excel|إكسل|اكسل/i.test(i))) {
  if (!(await page.getByRole('menuitem', { name, exact: true }).isVisible().catch(() => false))) await page.getByRole('button', { name: L === 'ar' ? /طباعة|تصدير/ : 'Print or export' }).click();
  const dl = page.waitForEvent('download', { timeout: 180000 });
  await page.getByRole('menuitem', { name, exact: true }).click();
  const d = await dl; const p = path.join(OUT, `list-export-${L}-${d.suggestedFilename().replace(/[^\w.-]+/g, '_')}`); await d.saveAs(p);
  let rows;
  if (p.endsWith('.xlsx')) rows = readFirstSheet(p); else rows = fs.readFileSync(p, 'utf8').replace(/^﻿/, '').split(/\r?\n/).filter(Boolean).map(l => l.split(','));
  log(`${name} -> ${path.basename(p)}: ${rows.length - 1} rows; header: ${rows[0].join(' | ')}; first: ${(rows[1] || []).join(' | ').slice(0, 200)}`);
  if (!p.endsWith('.xlsx')) {} else fs.unlinkSync(p);
}
await browser.close();
