// Critic p05 r5: (a) reactivate the 57 khalid tariq users; (b) all-matching selection over many rows: confirmation with count, then activate (no-op).
import { open, focus, shot } from './pw.mjs';
const { browser, page } = await open('admin@alnoor.example');
const log = (...a) => console.log(...a);
const main = () => page.locator('main').innerText();
page.on('dialog', async d => { log('   native dialog:', d.type(), JSON.stringify(d.message())); await d.dismiss(); });
let puts = 0, posts = [];
page.on('response', r => { const m = r.request().method(); if (m === 'PUT') puts++; if (m === 'POST') posts.push(r.url().replace('http://localhost:20550', '') + ' ' + r.status()); });
async function selectAll(q) {
  await page.goto('http://localhost:20550/identity/users'); await page.waitForTimeout(1500);
  await page.keyboard.type(q, { delay: 20 }); await page.waitForTimeout(1500);
  await page.keyboard.press('ArrowDown'); await page.keyboard.press('Control+a'); await page.waitForTimeout(200); await page.keyboard.press('Control+a'); await page.waitForTimeout(400);
  return ((await main()).match(/All [^\n]*selected|\d+ selected/) || ['?'])[0];
}
log('a', await selectAll('khalid tariq'));
await page.getByRole('button', { name: 'Activate', exact: true }).click(); await page.waitForTimeout(3000);
log('  result', ((await main()).match(/[^\n]*(changed|already)[^\n]*/i) || ['?'])[0], 'PUTs', puts);
puts = 0;
log('b', await selectAll('khalid'));
const t0 = Date.now();
await page.getByRole('button', { name: 'Deactivate', exact: true }).click(); await page.waitForTimeout(1200);
const dlg = page.locator('[role=dialog], [role=alertdialog]');
log('  in-page dialog', await dlg.count(), (await dlg.last().innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 300), 'focus', await focus(page));
await shot(page, '11-bulk-confirm-count.jpg');
await page.keyboard.press('Escape'); await page.waitForTimeout(500);
log('  after Esc: selection', ((await main()).match(/All [^\n]*selected|\d+ selected/) || ['none'])[0], 'PUTs', puts, 'POSTs', posts);
await browser.close();
