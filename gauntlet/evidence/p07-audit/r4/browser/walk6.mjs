import { chromium } from '/home/shan/critic/p07-audit-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20750', OUT = '/home/shan/evidence-staging/p07-audit/r4/browser';
const lang = process.argv[2] || 'en';
const email = lang === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example';
const steps = JSON.parse(process.argv[3]); const shotName = process.argv[4];
const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
const errors = []; page.on('response', async r => { if (r.status() >= 400) errors.push(`${r.status()} ${decodeURIComponent(r.url()).slice(0, 200)}`); }); page.on('pageerror', e => errors.push('pageerror ' + e.message));
const apis = []; page.on('requestfinished', r => { const u = decodeURIComponent(r.url()); if (u.includes('/api/audit/log?') && u.includes('filter=')) apis.push(`${Math.round(r.timing().responseEnd)}ms ${u.replace(BASE, '').slice(0, 220)}`); });
await page.goto(BASE + '/');
await page.locator('input[name="email"]').fill(email);
await page.locator('input[name="password"]').fill('Demo-Pass-2026');
await page.keyboard.press('Enter');
await page.waitForSelector('nav'); await page.waitForTimeout(800);
await page.keyboard.press('g'); await page.keyboard.press('l');
await page.waitForSelector('[role="grid"] [role="row"] >> nth=3', { timeout: 30000 });
const count = async () => page.evaluate(() => document.querySelector('main [aria-live], .list-count')?.innerText || [...document.querySelectorAll('main *')].find(e => e.childElementCount === 0 && /[\d٠-٩]/.test(e.innerText || '') && /change|تغيير/.test(e.innerText))?.innerText);
for (const [col, kind, value, cond] of steps) {
  await page.locator(`button[aria-label$="${col}"]`).first().focus();
  await page.keyboard.press('Enter'); await page.waitForTimeout(300);
  await page.keyboard.press('Enter'); await page.waitForSelector('[role="dialog"]');
  if (kind === 'check') {
    for (const v of [].concat(value)) {
      const box = page.locator('[role="dialog"] label').filter({ has: page.locator('input[type=checkbox]') }).filter({ hasText: new RegExp(`^\\s*${v.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*$`) }).first().locator('input');
      await box.focus(); await page.keyboard.press('Space');
    }
  } else if (kind === 'text') {
    if (cond) { await page.locator('[role="dialog"] select').first().focus(); await page.locator('[role="dialog"] select').first().selectOption({ label: cond }); }
    await page.locator('[role="dialog"] input[type=text], [role="dialog"] input:not([type])').first().focus(); await page.keyboard.type(value);
  } else if (kind === 'raw') {
    const html = await page.evaluate(() => document.querySelector('[role="dialog"]').outerHTML.slice(0, 1500)); console.log(html);
    for (const k of value) { if (k.startsWith('key:')) await page.keyboard.press(k.slice(4)); else await page.keyboard.type(k); }
  }
  await page.locator('[role="dialog"] button[type=submit]').focus(); await page.keyboard.press('Enter');
  await page.waitForTimeout(1500);
  console.log(col, '->', await count(), '| row 1:', (await page.locator('[role="grid"] [role="row"]').nth(1).innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 170));
}
if (shotName) await page.screenshot({ path: `${OUT}/${shotName}.jpg`, type: 'jpeg', quality: 70 });
console.log(apis.join('\n')); console.log('errors', JSON.stringify(errors));
await browser.close();
