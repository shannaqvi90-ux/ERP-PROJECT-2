// Critic p05 r5: save a shared default view by keyboard; selection; all-matching bulk deactivate with confirmation (English).
import { open, focus, shot } from './pw.mjs';
const { browser, page } = await open('admin@alnoor.example');
const log = (...a) => console.log(...a);
const main = () => page.locator('main').innerText();
const count = async () => ((await main()).match(/(\d[\d,]*) users|one user|No users/) || ['?'])[0];
const tabTo = async (re, max = 40) => { for (let i = 0; i < max; i++) { if (re.test(await focus(page))) return true; await page.keyboard.press('Tab'); } return false; };
await page.goto('http://localhost:20550/identity/users?filter=' + encodeURIComponent("language eq 'ar'")); await page.waitForTimeout(2000);
await page.keyboard.type('khalid tariq', { delay: 30 }); await page.waitForTimeout(1500);
log('0 count', await count(), 'url', decodeURIComponent(page.url().split('/').pop()));
await tabTo(/button "View:/); await page.keyboard.press('Enter'); await page.waitForTimeout(300);
await page.keyboard.press('End'); log('1 End in view menu ->', await focus(page));
await page.keyboard.press('Enter'); await page.waitForTimeout(500);
log('2 save dialog', (await page.locator('[role=dialog]').last().innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 300), 'focus', await focus(page));
await page.keyboard.type('Critic Arabic Khalids');
for (let i = 0; i < 6; i++) { await page.keyboard.press('Tab'); const f = await focus(page); log('   tab', f); if (/share|everyone/i.test(f)) { await page.keyboard.press('Space'); log('   checked share'); } if (/default/i.test(f)) { await page.keyboard.press('Space'); log('   checked default'); } if (/button "Save/.test(f)) break; }
await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
log('3 after save: view button', (await page.getByRole('button', { name: /View:/ }).innerText()).replace(/\s+/g, ' '));
await shot(page, '09-saved-shared-view.jpg');
// Reload the list with no query: default view opens?
await page.goto('http://localhost:20550/identity/users'); await page.waitForTimeout(2000);
log('4 reopen: view', (await page.getByRole('button', { name: /View:/ }).innerText()).replace(/\s+/g, ' '), 'count', await count(), 'url', decodeURIComponent(page.url().split('/').pop()));
// Selection: into grid, Space, Shift+Down, Ctrl+A twice
await page.locator('input').first().focus(); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Space'); await page.keyboard.press('Shift+ArrowDown');
log('5 selection', ((await main()).match(/\d[\d,]* (selected|chosen)[^\n]*/i) || ['?'])[0]);
await page.keyboard.press('Control+a'); await page.waitForTimeout(300); log('6 ctrl+a', ((await main()).match(/[^\n]*(selected|chosen)[^\n]*/i) || ['?'])[0]);
await page.keyboard.press('Control+a'); await page.waitForTimeout(300); log('7 ctrl+a x2', ((await main()).match(/[^\n]*(selected|chosen|match)[^\n]*/i) || ['?'])[0]);
const bulk = await page.getByRole('button').allInnerTexts(); log('   buttons', bulk.filter(b => /Activate|Deactivate|Copy|Clear|Export|Print/.test(b)).join(' | '));
await shot(page, '10-all-matching-selected.jpg');
const deact = page.getByRole('button', { name: 'Deactivate', exact: true });
log('   Deactivate enabled?', await deact.isEnabled());
await deact.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(600);
log('8 confirm', (await page.locator('[role=dialog], [role=alertdialog]').last().innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 300), 'focus', await focus(page));
await shot(page, '11-bulk-confirm-count.jpg');
await page.keyboard.press('Escape'); await page.waitForTimeout(400);
log('9 cancelled; selection kept?', ((await main()).match(/[^\n]*(selected|chosen|match)[^\n]*/i) || ['?'])[0]);
await browser.close();
