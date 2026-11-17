// Screenshots 16 views per direction (+ one high-contrast view) at 1440x900 with headless Chromium.
// Usage: node shoot.mjs [a|b|c ...]   (needs playwright-core; set PW to its path if not resolvable)
import { createRequire } from 'node:module';
import fs from 'node:fs';
const require = createRequire(import.meta.url);
const pw = require(process.env.PW || 'playwright-core');
const root = new URL('../', import.meta.url).pathname;
const exe = process.env.CHROME || `${process.env.HOME}/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome`;
const want = process.argv.slice(2);
const pages = ['contacts', 'contact-form', 'settings', 'sign-in'];
const browser = await pw.chromium.launch({ executablePath: exe, args: ['--allow-file-access-from-files'] });
try {
  for (const d of ['a', 'b', 'c']) {
    if (want.length && !want.includes(d)) continue;
    fs.mkdirSync(`${root}${d}/screenshots`, { recursive: true });
    const jobs = [];
    for (const p of pages) for (const l of ['en', 'ar']) for (const t of ['light', 'dark']) jobs.push([p, l, t]);
    jobs.push(['contacts', 'en', 'hc'], ['contacts', 'ar', 'hc']);
    const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
    const page = await ctx.newPage();
    for (const [p, l, t] of jobs) {
      await page.goto(`file://${root}${d}/${p}.${l}.html?theme=${t}`);
      await page.evaluate(() => document.fonts.ready);
      await page.waitForTimeout(150);
      await page.screenshot({ path: `${root}${d}/screenshots/${p}.${l}.${t}.png` });
    }
    await ctx.close();
  }
} finally { await browser.close(); }
