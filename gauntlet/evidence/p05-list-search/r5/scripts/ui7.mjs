// Critic p05 r5: all-matching bulk on a multi-row search: confirmation, timing, result; then activate back.
import { open, focus, shot } from './pw.mjs';
const { browser, page } = await open('admin@alnoor.example');
const log = (...a) => console.log(...a);
const main = () => page.locator('main').innerText();
page.on('dialog', async d => { log('   native dialog:', d.type(), JSON.stringify(d.message())); await d.accept(); });
page.on('response', r => { if (r.request().method() !== 'GET') log('   ', r.request().method(), r.url().replace('http://localhost:20550', ''), r.status()); });
await page.goto('http://localhost:20550/identity/users'); await page.waitForTimeout(1500);
await page.keyboard.type('khalid tariq', { delay: 20 }); await page.waitForTimeout(1500);
log('0', (await main()).split('\n').slice(0, 6).join(' / '));
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Control+a'); await page.waitForTimeout(200); await page.keyboard.press('Control+a'); await page.waitForTimeout(300);
log('1', ((await main()).match(/All [^\n]*selected|\d+ selected/) || ['?'])[0]);
for (const action of ['Deactivate', 'Activate']) {
  const btn = page.getByRole('button', { name: action, exact: true });
  log(action, 'enabled', await btn.isEnabled(), 'title', await btn.getAttribute('title'));
  let t0 = Date.now();
  await btn.click({ timeout: 5000 }).catch(e => log('   click failed', e.message.split('\n')[0]));
  log('   clicked after', Date.now() - t0, 'ms');
  await page.waitForTimeout(1500);
  const dlg = page.locator('[role=dialog], [role=alertdialog]');
  log('   in-page dialog', await dlg.count(), (await dlg.last().innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 300));
  if (await dlg.count()) { if (action === 'Deactivate') await shot(page, '11-bulk-confirm-count.jpg'); t0 = Date.now(); await dlg.last().getByRole('button', { name: new RegExp(action) }).click(); await page.waitForTimeout(1500); log('   confirmed, after', Date.now() - t0); }
  log('   result:', ((await main()).match(/[^\n]*(changed|already)[^\n]*/i) || ['?'])[0]);
  log('   statuses', (await page.getByRole('row').allInnerTexts()).slice(1).map(r => r.replace(/\s+/g, ' ').replace(/@staff.example/, '').slice(0, 60)).slice(0, 3));
  if (action === 'Deactivate') await shot(page, '12-bulk-deactivated.jpg');
}
await browser.close();
