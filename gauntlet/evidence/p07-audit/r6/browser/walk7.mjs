// r6 critic: audit log filters by keyboard: person picker, record type, field, record, date. Usage: node walk7.mjs <en|ar> <person> <recordTypeLabel> <fieldLabel> <shotprefix>
import { chromium } from '/home/shan/critic/p07-audit-r6/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20750', OUT = '/home/shan/evidence-staging/p07-audit/r6/browser';
const [lang, person, typeLabel, fieldLabel, shot, downs = '1'] = process.argv.slice(2);
const email = lang === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example';
const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
const errors = []; page.on('response', r => { if (r.status() >= 400) errors.push(`${r.status()} ${decodeURIComponent(r.url()).slice(0, 220)}`); }); page.on('pageerror', e => errors.push('pageerror ' + e.message));
const apis = []; page.on('requestfinished', r => { const u = decodeURIComponent(r.url()); if (u.includes('/api/audit/')) apis.push(`${Math.round(r.timing().responseEnd)}ms ${u.replace(BASE, '').slice(0, 240)}`); });
await page.goto(BASE + '/');
await page.locator('input[name="email"]').fill(email); await page.locator('input[name="password"]').fill('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.waitForSelector('nav'); await page.waitForTimeout(800);
await page.keyboard.press('g'); await page.keyboard.press('l');
await page.waitForSelector('[role="grid"] [role="row"] >> nth=3', { timeout: 30000 });
const count = () => page.evaluate(() => [...document.querySelectorAll('main *')].find(e => e.childElementCount === 0 && /[\d٠-٩]|لا /.test(e.innerText || '') && /change|تغيير/.test(e.innerText))?.innerText);
const row = async n => (await page.locator('[role="grid"] [role="row"]').nth(n).innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 200);
async function openFilter(col) {
  await page.locator('[role="columnheader"] button[aria-label]').filter({ hasText: '' }).first().count();
  const btn = page.locator(`button[aria-label$="${col}"]`).first();
  await btn.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(300); await page.keyboard.press('Enter');
  await page.waitForSelector('[role="dialog"]');
}
async function apply() { await page.locator('[role="dialog"] button[type=submit]').focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(1800); }
const t0 = await count(); console.log('start', t0);
// 1. Changed by: person picker, typed and chosen by keyboard.
await openFilter(lang === 'ar' ? 'بواسطة' : 'Changed by');
await page.locator('[role="dialog"] input[role=combobox]').focus();
await page.keyboard.type(person); await page.waitForTimeout(1500);
console.log('options', JSON.stringify(await page.locator('[role="dialog"] [role=option]').allInnerTexts()).slice(0, 400));
for (let i = 0; i < +downs; i++) await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(400);
console.log('chip/input', await page.locator('[role="dialog"] input[role=combobox]').inputValue(), '| active', await page.evaluate(() => document.activeElement?.outerHTML.slice(0, 150)));
console.log('dialog after choose', (await page.locator('[role="dialog"]').innerText()).replace(/\s+/g, ' ').slice(0, 300));
await apply();
console.log('Changed by ->', await count(), '| row1', await row(1), '| row2', await row(2));
if (shot) await page.screenshot({ path: `${OUT}/${shot}-a-changed-by.jpg`, type: 'jpeg', quality: 65 });
// 2. Record type
await openFilter(lang === 'ar' ? 'نوع السجل' : 'Record type');
const box = page.locator('[role="dialog"] label').filter({ has: page.locator('input[type=checkbox]') }).filter({ hasText: new RegExp(`^\\s*${typeLabel}\\s*$`) }).first().locator('input');
await box.focus(); await page.keyboard.press('Space'); await apply();
console.log('Record type ->', await count(), '| row1', await row(1));
// 3. Fields
await openFilter(lang === 'ar' ? 'الحقول' : 'Fields');
const labels = await page.locator('[role="dialog"] label').allInnerTexts();
const dup = labels.map(s => s.trim()).filter((v, i, a) => a.indexOf(v) !== i);
console.log('field choices', labels.length, 'duplicates', JSON.stringify([...new Set(dup)]));
const fbox = page.locator('[role="dialog"] label').filter({ has: page.locator('input[type=checkbox]') }).filter({ hasText: new RegExp(`^\\s*${fieldLabel}\\s*$`) }).first().locator('input');
await fbox.focus(); await page.keyboard.press('Space'); await apply();
console.log('Fields ->', await count(), '| row1', await row(1));
if (shot) await page.screenshot({ path: `${OUT}/${shot}-b-three-filters.jpg`, type: 'jpeg', quality: 65 });
console.log(apis.slice(-12).join('\n')); console.log('errors', JSON.stringify(errors));
await browser.close();
