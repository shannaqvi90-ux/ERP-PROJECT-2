// Screenshots every page of a direction in English and Arabic, light and dark, at 1440x900 with
// headless Chromium, plus the high-contrast contacts views and each direction's extra views.
// Usage: node shoot.mjs [a|b|c|d|e|f|g ...]   ONLY=contacts,dashboard limits the pages.
// (needs playwright-core; set PW to its path if not resolvable)
import { createRequire } from 'node:module';
import fs from 'node:fs';
const require = createRequire(import.meta.url);
const pw = require(process.env.PW || 'playwright-core');
const root = new URL('../', import.meta.url).pathname;
const exe = process.env.CHROME || `${process.env.HOME}/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome`;
const want = process.argv.slice(2);
const only = process.env.ONLY ? process.env.ONLY.split(',') : null;
const pages = ['dashboard', 'contacts', 'contact-form', 'settings', 'sign-in'];
// Extra views: [page, query, screenshot name]; the theme and language are appended.
const hcAll = ['g'];
const extra = { d: [['contacts', 'palette=open', 'command-palette']], g: [['contacts', 'palette=open', 'command-palette']] };
const browser = await pw.chromium.launch({ executablePath: exe, args: ['--allow-file-access-from-files'] });
try {
  for (const d of ['a', 'b', 'c', 'd', 'e', 'f', 'g']) {
    if (want.length && !want.includes(d)) continue;
    if (!fs.existsSync(`${root}${d}`)) continue;
    fs.mkdirSync(`${root}${d}/screenshots`, { recursive: true });
    const jobs = [];
    for (const p of pages) {
      if (!fs.existsSync(`${root}${d}/${p}.en.html`) || (only && !only.includes(p))) continue;
      for (const l of ['en', 'ar']) for (const t of ['light', 'dark']) jobs.push([p, l, t, '', p]);
    }
    if (!only || only.includes('contacts') || only.includes('hc')) jobs.push(['contacts', 'en', 'hc', '', 'contacts'], ['contacts', 'ar', 'hc', '', 'contacts']);
    // Directions listed in hcAll get a high-contrast shot of every page and extra view, not only contacts.
    if (hcAll.includes(d)) for (const p of pages) if (p !== 'contacts' && fs.existsSync(`${root}${d}/${p}.en.html`) && (!only || only.includes(p) || only.includes('hc'))) for (const l of ['en', 'ar']) jobs.push([p, l, 'hc', '', p]);
    for (const [p, q, n] of extra[d] || []) if (!only || only.includes(n)) for (const l of ['en', 'ar']) for (const t of hcAll.includes(d) ? ['dark', 'light', 'hc'] : ['dark', 'light']) jobs.push([p, l, t, q, n]);
    const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
    const page = await ctx.newPage();
    for (const [p, l, t, q, n] of jobs) {
      await page.goto(`file://${root}${d}/${p}.${l}.html?theme=${t}${q ? '&' + q : ''}`);
      await page.evaluate(() => document.fonts.ready);
      await page.waitForTimeout(150);
      await page.screenshot({ path: `${root}${d}/screenshots/${n}.${l}.${t}.png` });
    }
    await ctx.close();
  }
} finally { await browser.close(); }
