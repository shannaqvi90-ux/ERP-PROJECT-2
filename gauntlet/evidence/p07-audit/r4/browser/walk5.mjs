import { chromium } from '/home/shan/critic/p07-audit-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20750', OUT = '/home/shan/evidence-staging/p07-audit/r4/browser';
const lang = process.argv[2] || 'en';
const email = lang === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example';
const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
const errors = []; page.on('response', async r => { if (r.status() >= 400) errors.push(`${r.status()} ${r.url().slice(0, 200)}`); }); page.on('pageerror', e => errors.push('pageerror ' + e.message));
const apis = []; page.on('requestfinished', r => { if (r.url().includes('/api/audit/log')) apis.push(`${Math.round(r.timing().responseEnd)}ms ${decodeURIComponent(r.url().replace(BASE, '')).slice(0, 220)}`); });
await page.goto(BASE + '/');
await page.locator('input[name="email"]').fill(email);
await page.locator('input[name="password"]').fill('Demo-Pass-2026');
await page.keyboard.press('Enter');
await page.waitForSelector('nav'); await page.waitForTimeout(800);
await page.keyboard.press('g'); await page.keyboard.press('l');
await page.waitForSelector('[role="grid"] [role="row"] >> nth=3', { timeout: 30000 });
const count = async () => page.evaluate(() => [...document.querySelectorAll('main *')].find(e => e.childElementCount === 0 && /changes|تغيير|تغييرات/.test(e.innerText || '') && /[\d٠-٩]/.test(e.innerText))?.innerText);
const filters = JSON.parse(process.argv[3]);
let shot = 6;
for (const [col, value, mode] of filters) {
  // Keyboard: focus the column's options button (Tab order), Enter, Enter on Filter…
  const btn = page.locator(`button[aria-label$="${col}"]`).first();
  await btn.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(300); await page.keyboard.press('Enter'); await page.waitForTimeout(500);
  const dlg = await page.evaluate(() => { const d = document.querySelector('[role="dialog"]'); return d ? { text: d.innerText.replace(/\n+/g, ' / ').slice(0, 400), inputs: [...d.querySelectorAll('input,select')].map(i => i.tagName + ':' + (i.type || '') + ':' + (i.getAttribute('aria-label') || i.closest('label')?.innerText || '').slice(0, 40)) } : null; });
  console.log('FILTER', col, JSON.stringify(dlg));
  if (mode === 'type') { await page.keyboard.type(value); await page.waitForTimeout(800); }
  else if (mode === 'option') { const o = page.locator('[role="dialog"] label', { hasText: value }).first(); await o.focus().catch(() => {}); await page.locator('[role="dialog"]').getByLabel(value, { exact: true }).first().check().catch(e => console.log('check failed', e.message.slice(0, 80))); }
  else if (mode === 'select') { await page.locator('[role="dialog"] select').first().selectOption({ label: value }).catch(e => console.log('select failed', e.message.slice(0, 100))); }
  await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
  console.log('  ->', await count(), '| first rows:', (await page.locator('[role="grid"] [role="row"]').nth(1).innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 160));
  if (shot <= 7 && process.env.SHOTS) await page.screenshot({ path: `${OUT}/${lang}-0${shot++}-filter-${col.replace(/\W+/g, '')}.jpg`, type: 'jpeg', quality: 70 });
}
console.log(apis.join('\n')); console.log('errors', JSON.stringify(errors));
await browser.close();
