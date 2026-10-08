// Critic p05 r5: all-matching bulk deactivate on a search (confirmation, result, audit), then reactivate.
import { open, focus, shot } from './pw.mjs';
const { browser, page } = await open('admin@alnoor.example');
const log = (...a) => console.log(...a);
const main = () => page.locator('main').innerText();
page.on('dialog', async d => { log('   native dialog:', d.type(), JSON.stringify(d.message())); await d.accept(); });
await page.goto('http://localhost:20550/identity/users'); await page.waitForTimeout(1500);
await page.keyboard.type('khalid tariq al falasi', { delay: 20 }); await page.waitForTimeout(1500);
log('0', ((await main()).match(/(\d[\d,]*) users|one user|No users/) || ['?'])[0]);
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Control+a'); await page.keyboard.press('Control+a'); await page.waitForTimeout(300);
log('1', ((await main()).match(/All [^\n]*selected|\d+ selected/) || ['?'])[0]);
const t0 = Date.now();
await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
await page.waitForTimeout(800);
const dlg = page.locator('[role=dialog], [role=alertdialog]');
log('2 in-page dialog count', await dlg.count(), (await dlg.last().innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 300));
if (await dlg.count()) { await shot(page, '11-bulk-confirm-count.jpg'); const b = dlg.last().getByRole('button', { name: /Deactivate/ }); await b.click(); await page.waitForTimeout(1500); }
log('3 result', ((await main()).match(/[^\n]*(deactivated|changed|users? (now|were))[^\n]*/i) || ['?'])[0], (Date.now() - t0) + 'ms');
log('   statuses', (await page.getByRole('row').allInnerTexts()).slice(1).map(r => r.replace(/\s+/g, ' ').slice(0, 70)).slice(0, 5));
await shot(page, '12-bulk-deactivated.jpg');
await browser.close();
