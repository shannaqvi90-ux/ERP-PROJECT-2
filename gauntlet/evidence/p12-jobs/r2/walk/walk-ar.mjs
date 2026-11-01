import { open, shot } from './common.mjs';
const log = (...a) => console.log(...a);
{
  const { browser, page } = await open('admin.ar@alnoor.example');
  log('lang/dir', await page.evaluate(() => [document.documentElement.lang, document.documentElement.dir]));
  await page.keyboard.press('g'); await page.keyboard.press('j');
  await page.waitForFunction(() => document.querySelectorAll('table[role=grid] tbody tr').length > 0);
  log('h1', await page.locator('main h1').innerText(), 'headers', await page.evaluate(() => [...document.querySelectorAll('table[role=grid] thead th')].map(t => t.textContent.trim())));
  log('rows', await page.evaluate(() => [...document.querySelectorAll('table[role=grid] tbody tr')].slice(0, 3).map(r => r.textContent.slice(0, 120))));
  await shot(page, '08-jobs-list-ar');
  // Open the newest finished self-check ("فحص ذاتي") and look at the panel in Arabic.
  await page.keyboard.type('فحص ذاتي');
  await page.waitForTimeout(1200);
  await page.keyboard.press('Enter');
  await page.locator('.list-record').waitFor();
  await page.waitForTimeout(800);
  log('panel ar:', (await page.locator('.list-record').innerText()).slice(0, 900));
  await shot(page, '09-job-panel-ar');
  // A failed seeded job in Arabic: search a seeded title and step to the first failed.
  await page.keyboard.press('Escape');
  await page.locator('input.search').focus();
  await page.keyboard.press('Control+a'); await page.keyboard.press('Backspace');
  await page.keyboard.type('ملخص التدقيق');
  await page.waitForTimeout(1200);
  const st = await page.evaluate(() => [...document.querySelectorAll('table[role=grid] tbody tr')].map(r => r.textContent));
  const fi = st.findIndex(s => /فشل|فشلت|خطأ/.test(s));
  log('ar statuses', st.slice(0, 6).map(s => s.slice(0, 80)), 'failed idx', fi);
  if (fi >= 0) { await page.keyboard.press('ArrowDown'); for (let i = 0; i < fi; i++) await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.locator('.list-record').waitFor(); await page.waitForTimeout(800);
    log('failed panel ar:', (await page.locator('.list-record').innerText()).slice(0, 700)); await shot(page, '10-failed-job-ar'); }
  await page.keyboard.press('Escape');
  await page.locator('body').press('Escape');
  await page.keyboard.press('g'); await page.keyboard.press('s');
  await page.waitForTimeout(2000);
  log('schedules ar:', (await page.locator('main').innerText()).slice(0, 400));
  await browser.close();
}
{
  const { browser, page } = await open('viewer@alnoor.example');
  await page.keyboard.press('g'); await page.keyboard.press('j');
  await page.waitForFunction(() => document.querySelectorAll('table[role=grid] tbody tr').length > 0, null, { timeout: 15000 }).catch(() => log('viewer: no rows'));
  log('viewer url', page.url(), 'buttons', await page.evaluate(() => [...document.querySelectorAll('main button')].map(b => b.textContent.trim()).filter(Boolean).slice(0, 12)));
  await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
  await page.waitForTimeout(1500);
  const before = page.url();
  await page.keyboard.press('Alt+r'); await page.keyboard.press('Alt+k'); await page.keyboard.press('Alt+n');
  await page.waitForTimeout(1500);
  log('viewer after Alt+R/K/N: url same', page.url() === before, 'dialogs', await page.locator('[role=dialog]').count(), 'panel buttons', await page.evaluate(() => [...document.querySelectorAll('.list-record button, .list-record a.button')].map(b => b.textContent.trim())));
  await browser.close();
}
