import { open, shot } from './common.mjs';
const log = (...a) => console.log(...a);
const { browser, page } = await open('admin@alnoor.example');
const rows = () => page.evaluate(() => document.querySelectorAll('table[role=grid] tbody tr').length);
await page.keyboard.press('g'); await page.keyboard.press('j');
await page.waitForFunction(() => document.querySelectorAll('table[role=grid] tbody tr').length > 0);
log('search hint:', await page.evaluate(() => document.getElementById(document.activeElement.getAttribute('aria-describedby'))?.textContent));
await shot(page, '01-jobs-list-en');
// A failed job: type a filter in the search box.
const t0 = Date.now();
await page.keyboard.type("status:failed");
await page.waitForTimeout(1500);
log('rows after "status:failed":', await rows(), 'first:', await page.evaluate(() => document.querySelector('table[role=grid] tbody tr')?.textContent.slice(0, 120)), 'main text count:', await page.evaluate(() => document.querySelector('main')?.innerText.match(/[\d,]+ jobs[^\n]*/i)?.[0]));
await page.keyboard.press('Control+a'); await page.keyboard.press('Backspace');
await page.keyboard.type('Nightly audit digest');
await page.waitForTimeout(1200);
log('rows for digest', await rows(), await page.evaluate(() => document.querySelector('main')?.innerText.match(/[\d,]+ jobs[^\n]*/i)?.[0]));
// Open a failed one: go down the list with the arrow keys to the first row whose status is Failed.
const statuses = await page.evaluate(() => [...document.querySelectorAll('table[role=grid] tbody tr')].map(r => r.textContent));
log('statuses sample', statuses.slice(0, 8).map(s => s.slice(0, 80)));
const failedIndex = statuses.findIndex(s => /Failed/.test(s));
log('failed row index', failedIndex);
await page.keyboard.press('ArrowDown');
for (let i = 0; i < failedIndex; i++) await page.keyboard.press('ArrowDown');
await page.keyboard.press('Enter');
await page.locator('.list-record').waitFor({ timeout: 10000 });
await page.waitForTimeout(800);
log('panel text:', (await page.locator('.list-record').innerText()).slice(0, 1500));
await shot(page, '02-failed-job-panel-en');
// Run the failed job again by Alt+R.
const before = page.url();
await page.keyboard.press('Alt+r');
await page.waitForFunction(b => location.href !== b, before, { timeout: 10000 }).catch(() => log('url did not change after Alt+R'));
await page.waitForFunction(() => /Done|Failed/.test(document.querySelector('.list-record .jb-status')?.textContent ?? ''), null, { timeout: 60000 }).catch(() => log('rerun did not end in 60 s'));
log('after Alt+R:', page.url(), (await page.locator('.list-record').innerText()).slice(0, 600));
await shot(page, '03-rerun-ended-en');
// Start a slow self-check with Alt+N, watch it, cancel it with Alt+K.
await page.keyboard.press('Alt+n');
await page.locator('[role=dialog]').waitFor();
log('start dialog:', (await page.locator('[role=dialog]').innerText()).slice(0, 800));
log('dialog fields:', await page.evaluate(() => [...document.querySelectorAll('[role=dialog] input, [role=dialog] select, [role=dialog] textarea')].map(e => `${e.tagName} name=${e.name} value=${(e.value||'').slice(0,80)} focused=${e === document.activeElement}`)));
await browser.close();
