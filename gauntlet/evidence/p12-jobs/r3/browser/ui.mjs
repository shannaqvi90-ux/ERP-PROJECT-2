// Critic p12-jobs r3: the jobs screens in a real browser, by keyboard, in English and Arabic.
import { chromium } from '/home/shan/critic/p12-jobs-r3-cmp/gauntlet/compare/node_modules/playwright-core/index.mjs';
import fs from 'node:fs';
const base = process.argv[2] ?? 'http://localhost:21250';
const dir = new URL('.', import.meta.url).pathname;
const shots = dir + '../';
const lines = [];
const log = (...a) => { const s = a.join(' '); lines.push(s); console.log(s); };
const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });

async function session(email, lang) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, acceptDownloads: true, locale: lang === 'ar' ? 'ar-AE' : 'en-US' });
  const page = await context.newPage();
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
const shot = async (page, name) => { await page.screenshot({ path: shots + name, type: 'jpeg', quality: 70 }); log('shot', name); };
const text = async (page, sel) => (await page.locator(sel).first().textContent().catch(() => null))?.trim();

// English, administrator.
{
  const { context, page } = await session('admin@alnoor.example', 'en');
  await page.keyboard.press('g'); await page.keyboard.press('j');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  log('EN list heading:', await text(page, 'main h1'), '| grid:', await page.locator('table[role=grid]').getAttribute('aria-label'), '| url', page.url());
  log('EN columns:', (await page.locator('table[role=grid] thead th').allTextContents()).join(' | '));
  await shot(page, '01-jobs-list-en.jpg');
  // Start a job by keyboard: Alt+N, choose self-check, parameters, Ctrl+Enter.
  await page.keyboard.press('Alt+n');
  await page.waitForTimeout(800);
  const dialog = page.locator('[role=dialog]').first();
  log('Start dialog open:', await dialog.isVisible().catch(() => false), 'focused:', await page.evaluate(() => document.activeElement?.outerHTML.slice(0, 120)));
  // Choose a kind with the keyboard.
  const select = dialog.locator('select').first();
  if (await select.count()) { await select.focus(); await select.selectOption('jobs.selfCheck'); }
  const box = dialog.locator('textarea').first();
  if (await box.count()) { await box.focus(); await page.keyboard.press('Control+a'); await page.keyboard.type('{"steps": 12, "pauseMilliseconds": 900}'); }
  await shot(page, '02-start-dialog-en.jpg');
  await page.keyboard.press('Control+Enter');
  await page.waitForTimeout(2500);
  log('after Ctrl+Enter url', page.url(), 'dialog visible', await dialog.isVisible().catch(() => false));
  await page.waitForTimeout(3000);
  log('running panel status:', await text(page, '.list-record .jb-status'), '| progress:', await text(page, '.list-record [role=progressbar], .list-record .progress'));
  await shot(page, '03-job-running-progress-en.jpg');
  // Cancel with the keyboard.
  await page.keyboard.press('Alt+k');
  await page.waitForTimeout(800);
  const confirm = page.locator('[role=alertdialog], [role=dialog]').last();
  if (await confirm.isVisible().catch(() => false)) { log('cancel confirm:', (await confirm.textContent())?.slice(0, 120)); await page.keyboard.press('Enter'); }
  await page.waitForTimeout(1500);
  log('after first Alt+K: status', await text(page, '.list-record .jb-status'), '| panel message:', await text(page, '.list-record [role=alert], .list-record .error, .list-record .problem'));
  await page.keyboard.press('Alt+k');
  await page.waitForTimeout(3000);
  log('after cancel status:', await text(page, '.list-record .jb-status'));
  await shot(page, '04-job-cancelled-en.jpg');
  // Run again (Alt+R) then wait to finish; download output.
  await page.keyboard.press('Alt+r');
  await page.waitForTimeout(16000);
  log('rerun url', page.url(), 'status:', await text(page, '.list-record .jb-status'), 'finished:', await text(page, '.list-record [data-testid="job-finished"]'));
  const dl = page.locator('.list-record a[download], .list-record a[href*="/output"], .list-record button:has-text("Download")').first();
  if (await dl.count()) {
    const [download] = await Promise.all([page.waitForEvent('download', { timeout: 10000 }).catch(() => null), dl.click()]);
    if (download) { const p = dir + 'output-' + download.suggestedFilename(); await download.saveAs(p); log('output downloaded', download.suggestedFilename(), fs.readFileSync(p, 'utf8').replace(/\n/g, ' | ')); }
  } else log('no download control found');
  await shot(page, '05-rerun-done-output-en.jpg');
  // Filter failed: type a filter in the search? use the URL filter of the list.
  const failed = await page.evaluate(async () => (await (await fetch('/api/jobs/jobs?take=1&filter=' + encodeURIComponent("jobStatus eq 'failed' and jobType eq 'jobs.cleanup'"))).json()).items?.[0]?.id);
  log('a failed clean-up job', failed);
  await page.goto(base + '/jobs/jobs/' + failed);
  await page.locator('.list-record .jb-status').first().waitFor();
  await page.waitForTimeout(1500);
  log('failed job panel status:', await text(page, '.list-record .jb-status'), '| error text:', (await page.locator('.list-record').first().textContent())?.slice(0, 600));
  await shot(page, '06-failed-job-error-attempts-en.jpg');
  // Schedules.
  await page.goto(base + '/jobs/schedules');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  log('EN schedules rows:', (await page.locator('table[role=grid] tbody tr').allTextContents()).slice(0, 4).join(' || '));
  await shot(page, '07-schedules-en.jpg');
  await context.close();
}
// Arabic administrator.
{
  const { context, page } = await session('admin.ar@alnoor.example', 'ar');
  log('AR dir:', await page.evaluate(() => document.documentElement.dir), 'lang:', await page.evaluate(() => document.documentElement.lang));
  await page.keyboard.press('g'); await page.keyboard.press('j');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.waitForTimeout(800);
  log('AR heading:', await text(page, 'main h1'), '| columns:', (await page.locator('table[role=grid] thead th').allTextContents()).join(' | '));
  log('AR first rows:', (await page.locator('table[role=grid] tbody tr').allTextContents()).slice(0, 5).join(' || '));
  await shot(page, '08-jobs-list-ar.jpg');
  await page.keyboard.press('Enter').catch(() => {});
  await page.locator('input.search, input[type=search]').first().focus();
  await page.keyboard.type('self');
  await page.waitForTimeout(1500);
  await page.keyboard.press('Enter'); await page.waitForTimeout(400); await page.keyboard.press('Enter');
  await page.waitForTimeout(1500);
  log('AR panel:', (await page.locator('.list-record').first().textContent())?.slice(0, 500));
  await shot(page, '09-job-panel-ar.jpg');
  await page.goto(base + '/jobs/schedules');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.waitForTimeout(800);
  log('AR schedules rows:', (await page.locator('table[role=grid] tbody tr').allTextContents()).slice(0, 4).join(' || '));
  await shot(page, '10-schedules-ar.jpg');
  await context.close();
}
// Read-only user: screens hide what they cannot do.
{
  const { context, page } = await session('viewer@alnoor.example', 'en');
  await page.goto(base + '/jobs/jobs');
  await page.waitForTimeout(2500);
  log('viewer jobs screen heading:', await text(page, 'main h1'), '| buttons:', (await page.locator('main button').allTextContents()).join(' | ').slice(0, 300));
  await context.close();
}
await browser.close();
fs.writeFileSync(dir + 'ui.txt', lines.join('\n') + '\n');
