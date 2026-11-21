// Critic's import walk, p14 round 1: Contacts > Alt+I > file > mapping > Check only > error file > Import, in English and Arabic,
// by keyboard where the screen allows (the file itself comes from the system's file chooser).
// Run from gauntlet/compare of the critic clone: BASE=http://localhost:21450 OUT=<staging> LANG_RUN=en|ar node <staging>/import-walk.mjs
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
const tag = (L === 'ar' ? 'AR' : 'EN') + Date.now().toString().slice(-6);
const log = (...a) => console.log(`[${L}]`, ...a);
const shot = (page, name) => page.screenshot({ path: path.join(OUT, name), type: 'jpeg', quality: 60 });

const header = 'Client Name,E-mail address,Phone,Customer segment,Credit limit,Credit limit currency,Credit limit exchange rate';
const rows = [
  `Critic ${tag} Alpha LLC,alpha.${tag}@walk.example,+971 4 111 2222,keyAccount,1000,USD,`,
  `Critic ${tag} Beta LLC,beta.${tag}@walk.example,+971 4 111 3333,,2500.50,AED,`,
  `Critic ${tag} Gamma LLC,not-an-email,+971 4 111 4444,NoSuchSegment,abc,USD,3.6725`,
  `شركة ${tag} دلتا,delta.${tag}@walk.example,+971 4 111 5555,,"1,250.75",EUR,4.1`,
];
const file = path.join(OUT, `walk-${L}.csv`);
fs.writeFileSync(file, [header, ...rows].join('\r\n') + '\r\n');

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
await page.keyboard.press('g'); await page.keyboard.press('o');
await page.locator('[data-testid="import-open"]').waitFor();
log('dir', await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang));
await page.keyboard.press('Alt+i');
await page.locator('[data-testid="import-file"]').waitFor({ state: 'attached' });
log('dialog focus', await page.evaluate(() => document.activeElement?.outerHTML.slice(0, 120)));
await page.locator('[data-testid="import-file"]').setInputFiles(file);
await page.locator('[data-testid="import-mapping"]').waitFor();
await page.waitForTimeout(500);
const mapping = async () => page.locator('[data-testid="import-mapping"] tbody tr').evaluateAll(trs => trs.map(tr => tr.innerText.replace(/\s+/g, ' ').trim() + ' => ' + (tr.querySelector('select')?.selectedOptions[0]?.textContent ?? '')));
log('mapping', JSON.stringify(await mapping()));
// Column 1 ("Client Name") is not a field's name: choose Name for it by keyboard (focus the select, type to pick).
const sel = page.locator('[data-testid="import-mapping"] tbody tr').first().locator('select');
const nameLabel = L === 'ar' ? null : 'Name';
const options = await sel.locator('option').allTextContents();
log('options', options.slice(0, 6).join(' | '));
await sel.focus();
await sel.selectOption({ index: options.findIndex((o, i) => i > 0 && (nameLabel ? o === nameLabel : true)) });
await page.waitForTimeout(800);
log('mapping after', JSON.stringify(await mapping()));
await shot(page, `${L === 'ar' ? '04-ar' : '01-en'}-import-mapping.jpg`);
// Check only (dry run) by keyboard: Tab to it from the run button area.
const checkButton = page.locator('.ie-dialog button, dialog button, [role="dialog"] button').filter({ hasText: L === 'ar' ? /^فحص فقط$/ : /^Check only$/ }).first();
await checkButton.focus(); await page.keyboard.press('Enter');
await page.locator('[data-testid="import-checked"], [data-testid="import-failed"]').first().waitFor({ timeout: 60000 });
log('checked', (await page.locator('[data-testid="import-checked"], [data-testid="import-failed"]').first().innerText()).replace(/\s+/g, ' '));
const issues = await page.locator('[data-testid="import-issues"]').innerText().catch(() => '');
log('issues', issues.replace(/\s+/g, ' ').slice(0, 900));
await shot(page, `${L === 'ar' ? '05-ar' : '02-en'}-import-check-report.jpg`);
const dl = page.waitForEvent('download', { timeout: 15000 }).catch(() => null);
await page.locator('[data-testid="import-errors"]').focus().catch(() => {});
await page.keyboard.press('Enter');
const d = await dl;
if (d) { const p = path.join(OUT, `walk-${L}-errors.csv`); await d.saveAs(p); log('error file', d.suggestedFilename(), fs.readFileSync(p, 'utf8').slice(0, 600).replace(/\r?\n/g, ' / ')); } else log('error file: no download');
// Skip rows with problems, then Import.
const skip = page.getByLabel(L === 'ar' ? 'تجاهل الصفوف التي بها مشكلات واستيراد الباقي' : 'Skip rows with problems and import the rest');
if (!(await skip.isChecked())) { await skip.focus(); await page.keyboard.press('Space'); }
await page.locator('[data-testid="import-run"]').focus(); await page.keyboard.press('Enter');
await page.locator('[data-testid="import-done"], [data-testid="import-failed"]').first().waitFor({ timeout: 60000 });
log('done', (await page.locator('[data-testid="import-done"], [data-testid="import-failed"]').first().innerText()).replace(/\s+/g, ' '));
await shot(page, `${L === 'ar' ? '05b-ar' : '03-en'}-import-done.jpg`);
await page.keyboard.press('Escape');
await page.waitForTimeout(500);
// Memory: the same headers again are mapped as before.
fs.writeFileSync(file.replace('.csv', '-again.csv'), [header, `Critic ${tag} Again LLC,again.${tag}@walk.example,,,,,`].join('\r\n'));
await page.keyboard.press('Alt+i');
await page.locator('[data-testid="import-file"]').setInputFiles(file.replace('.csv', '-again.csv'));
await page.locator('[data-testid="import-mapping"]').waitFor();
await page.waitForTimeout(500);
log('mapping second file', JSON.stringify(await mapping()));
await page.keyboard.press('Escape');
fs.writeFileSync(path.join(OUT, `walk-${L}.tag`), tag);
await browser.close();
