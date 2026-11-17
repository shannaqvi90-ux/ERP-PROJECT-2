// Critic p12-jobs r6 (from the r5 critic script): the jobs screens in a real browser, by keyboard, in English and Arabic.
// (structure adapted from the r3 critic's walk; run against the r5 demo)
import { chromium } from '/home/shan/critic/p12-jobs-r6-plant/gauntlet/compare/node_modules/playwright-core/index.mjs';
import fs from 'node:fs';
const base = process.argv[2] ?? 'http://localhost:21250';
const dir = new URL('.', import.meta.url).pathname;
const shots = dir + '../';
const lines = [];
const log = (...a) => { const s = a.join(' '); lines.push(s); console.log(s); };
const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const errors = [];
async function session(email, lang) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, acceptDownloads: true, locale: lang === 'ar' ? 'ar-AE' : 'en-US' });
  const page = await context.newPage();
  page.on('response', r => { if (r.status() >= 500) errors.push(`${r.status()} ${r.url()}`); });
  page.on('pageerror', e => errors.push('pageerror ' + e.message));
  await page.goto(base + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email);
  await page.keyboard.press('Tab');
  await page.keyboard.type('Demo-Pass-2026');
  await page.keyboard.press('Enter');
  await page.locator('nav[aria-label]').first().waitFor();
  await page.locator('main h1').waitFor();
  return { context, page };
}
const shot = async (page, name) => { await page.screenshot({ path: shots + name, type: 'jpeg', quality: 65 }); log('shot', name); };
const text = async (page, sel) => (await page.locator(sel).first().textContent({ timeout: 3000 }).catch(() => null))?.trim();
const focused = page => page.evaluate(() => { const e = document.activeElement; return e ? `${e.tagName} ${e.getAttribute('name') ?? ''} ${e.getAttribute('aria-label') ?? ''} ${(e.textContent ?? '').slice(0, 40)}` : null; });

for (const [email, lang, tag] of [['admin@alnoor.example', 'en', 'en'], ['admin.ar@alnoor.example', 'ar', 'ar']]) {
  const { context, page } = await session(email, lang);
  log(`== ${tag}: dir`, await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang));
  await page.keyboard.press('g'); await page.keyboard.press('j');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.waitForTimeout(800);
  log(tag, 'heading:', await text(page, 'main h1'), '| grid:', await page.locator('table[role=grid]').getAttribute('aria-label'), '| focus:', await focused(page));
  log(tag, 'columns:', (await page.locator('table[role=grid] thead th').allTextContents()).join(' | '));
  log(tag, 'first rows:', (await page.locator('table[role=grid] tbody tr').allTextContents()).slice(0, 3).join(' || '));
  if (tag === 'en') await shot(page, '01-jobs-list-en.jpg');
  // Start by keyboard: Alt+N, kind, parameters, Ctrl+Enter.
  await page.keyboard.press('Alt+n');
  await page.waitForTimeout(800);
  const dialog = page.locator('[role=dialog]').first();
  log(tag, 'start dialog:', await dialog.isVisible().catch(() => false), '| focus:', await focused(page), '| text:', (await dialog.textContent().catch(() => ''))?.slice(0, 300));
  const select = dialog.locator('select').first();
  if (await select.count()) { await select.focus(); await select.selectOption('jobs.selfCheck'); }
  const box = dialog.locator('textarea').first();
  if (await box.count()) { await box.focus(); await page.keyboard.press('Control+a'); await page.keyboard.type('{"steps": 12, "pauseMilliseconds": 900}'); }
  if (tag === 'en') await shot(page, '02-start-dialog-en.jpg');
  await page.keyboard.press('Control+Enter');
  await page.waitForTimeout(3500);
  log(tag, 'after Ctrl+Enter url', page.url(), '| status:', await text(page, '.list-record .jb-status'), '| progress:', await text(page, '.list-record [role=progressbar]'));
  await shot(page, `03-job-running-${tag}.jpg`);
  await page.keyboard.press('Alt+k');
  await page.waitForTimeout(800);
  const confirm = page.locator('[role=alertdialog]').last();
  if (await confirm.isVisible().catch(() => false)) { log(tag, 'cancel confirm:', (await confirm.textContent())?.slice(0, 160)); await page.keyboard.press('Enter'); }
  await page.waitForTimeout(4000);
  log(tag, 'after cancel status:', await text(page, '.list-record .jb-status'), '| panel:', (await text(page, '.list-record'))?.slice(0, 400));
  if (tag === 'en') await shot(page, '04-job-cancelled-en.jpg');
  await page.keyboard.press('Alt+r');
  await page.waitForTimeout(17000);
  log(tag, 'rerun url', page.url(), '| status:', await text(page, '.list-record .jb-status'), '| finished:', await text(page, '.list-record [data-testid="job-finished"]'));
  const dl = page.locator('.list-record a[href*="/output"], .list-record a[download]').first();
  if (await dl.count()) {
    const [download] = await Promise.all([page.waitForEvent('download', { timeout: 10000 }).catch(() => null), dl.click()]);
    if (download) { const p = dir + `output-${tag}-` + download.suggestedFilename(); await download.saveAs(p); log(tag, 'output downloaded', download.suggestedFilename(), fs.readFileSync(p, 'utf8').split('\n').slice(0, 3).join(' | ')); }
  } else log(tag, 'no download control found');
  await shot(page, `05-rerun-done-output-${tag}.jpg`);
  // Filter: failed jobs via the list's filter (URL), then open the first.
  await page.keyboard.press('Escape');
  const failed = await page.evaluate(async () => (await (await fetch('/api/jobs/jobs?take=1&filter=' + encodeURIComponent("jobStatus eq 'failed'"), { headers: { 'X-Erp-Request': '1' } })).json()).items?.[0]?.id);
  await page.goto(base + '/jobs/jobs/' + failed);
  await page.locator('.list-record .jb-status').first().waitFor();
  await page.waitForTimeout(1500);
  log(tag, 'failed job panel:', (await text(page, '.list-record'))?.slice(0, 700));
  if (tag === 'en') await shot(page, '06-failed-job-error-en.jpg');
  // Schedules: Alt+N adds one by keyboard.
  await page.goto(base + '/jobs/schedules');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.waitForTimeout(800);
  log(tag, 'schedules rows:', (await page.locator('table[role=grid] tbody tr').allTextContents()).slice(0, 4).join(' || '));
  await page.keyboard.press('Alt+n');
  await page.waitForTimeout(800);
  log(tag, 'new schedule focus:', await focused(page));
  await page.keyboard.type(`Critic ${tag} nightly self-check`);
  await page.keyboard.press('Tab');
  await page.keyboard.press(tag === 'ar' ? 'ArrowDown' : 'ArrowDown');
  const sel = page.locator('.list-record select').first();
  if (await sel.count()) await sel.selectOption('jobs.selfCheck');
  await page.keyboard.press('Tab');
  await page.keyboard.type('15 2 * * *');
  await page.keyboard.press('Control+s');
  await page.waitForTimeout(2500);
  log(tag, 'after save: url', page.url(), '| form:', (await text(page, '.list-record'))?.slice(0, 500));
  await shot(page, `07-schedule-saved-${tag}.jpg`);
  // Bad cron: validation message
  await page.keyboard.press('Alt+n'); await page.waitForTimeout(600);
  await page.keyboard.type('bad cron'); await page.keyboard.press('Tab');
  if (await sel.count()) await page.locator('.list-record select').first().selectOption('jobs.selfCheck');
  await page.locator('.list-record input[name="cron"]').fill('61 * * *').catch(() => {});
  await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
  log(tag, 'bad cron messages:', (await page.locator('.list-record [role=alert], .list-record .field-error, .list-record [id$="-error"]').allTextContents()).join(' | '));
  await page.keyboard.press('Escape'); await page.waitForTimeout(300); await page.keyboard.press('Escape');
  await context.close();
}
{
  const { context, page } = await session('viewer@alnoor.example', 'en');
  await page.goto(base + '/jobs/jobs');
  await page.waitForTimeout(2500);
  log('viewer heading:', await text(page, 'main h1'), '| buttons:', (await page.locator('main button').allTextContents()).join(' | ').slice(0, 300));
  await page.goto(base + '/jobs/schedules'); await page.waitForTimeout(1500);
  log('viewer schedules heading:', await text(page, 'main h1'));
  await context.close();
}
await browser.close();
log('5xx / page errors:', errors.length, errors.slice(0, 10).join(' ; '));
fs.writeFileSync(dir + 'ui.txt', lines.join('\n') + '\n');
