// Critic p12-jobs r6: the History tab of a cancelled job and of a failed seeded job, in Arabic and English, opened by keyboard.
import { chromium } from '/home/shan/critic/p12-jobs-r6-plant/gauntlet/compare/node_modules/playwright-core/index.mjs';
const base = 'http://localhost:21250';
const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
for (const [email, lang, histLabel] of [['admin.ar@alnoor.example', 'ar', 'السجل'], ['admin@alnoor.example', 'en', 'History']]) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: lang === 'ar' ? 'ar-AE' : 'en-US' });
  const page = await ctx.newPage();
  const errs = []; page.on('response', r => { if (r.status() >= 500) errs.push(r.status() + ' ' + r.url()); }); page.on('pageerror', e => errs.push('pageerror ' + e.message));
  await page.goto(base + '/'); await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.locator('main h1').waitFor();
  for (const filter of ["jobStatus eq 'cancelled' and startedBy eq 'user'", "jobStatus eq 'succeeded' and startedBy eq 'user'"]) {
    const id = await page.evaluate(async (f) => (await (await fetch('/api/jobs/jobs?take=1&filter=' + encodeURIComponent(f), { headers: { 'X-Erp-Request': '1' } })).json()).items?.[0]?.id, filter);
    await page.goto(base + '/jobs/jobs/' + id);
    await page.locator('.list-record .jb-status').first().waitFor(); await page.waitForTimeout(1200);
    const hist = page.locator('.list-record button', { hasText: histLabel }).first();
    if (await hist.count()) { await hist.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(1800); }
    const body = (await page.locator('.list-record').first().textContent()) ?? '';
    const raw = body.match(/\b(queued|running|succeeded|cancelled|failed|user|schedule|rerun|system|seed)\b/g);
    console.log(`${lang} ${filter}: history text: ${body.replace(/\s+/g, ' ').slice(0, 900)}`);
    console.log(`${lang} raw codes in panel: ${raw ? [...new Set(raw)].join(', ') : 'none'}`);
    if (filter.includes('cancelled')) await page.screenshot({ path: `../08-job-history-${lang}.jpg`, type: 'jpeg', quality: 65 });
  }
  console.log(lang, '5xx/page errors:', errs.length, errs.join(' ; '));
  await ctx.close();
}
await browser.close();
