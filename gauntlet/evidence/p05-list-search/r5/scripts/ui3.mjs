// Critic p05 r5: sort, filter from header, group, column chooser, saved views, selection and bulk, by keyboard (English).
import { open, focus, shot } from './pw.mjs';
const { browser, page } = await open('admin@alnoor.example');
const log = (...a) => console.log(...a);
const main = () => page.locator('main').innerText();
const count = async () => ((await main()).match(/(\d[\d,]*) users|one user|No users/) || ['?'])[0];
const firstRows = async n => (await page.getByRole('row').allInnerTexts()).slice(1, 1 + n).map(s => s.replace(/\s+/g, ' ').slice(0, 60));
const tabTo = async (re, max = 30) => { for (let i = 0; i < max; i++) { if (re.test(await focus(page))) return true; await page.keyboard.press('Tab'); } return false; };
await page.goto('http://localhost:20550/identity/users'); await page.waitForTimeout(2000);
// Sort by Name with Enter on the header
await tabTo(/button "Name"/); await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
log('A sort Name asc: url', page.url().split('/').pop(), await firstRows(2));
await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
log('A sort Name desc:', await firstRows(2));
// Column menu on Language: open with Enter, list items
await tabTo(/Options for the column Language/); await page.keyboard.press('Enter'); await page.waitForTimeout(500);
log('B Language menu focus', await focus(page), 'items', (await page.getByRole('menuitem').allInnerTexts()).join(' | '));
await shot(page, '04-column-menu.jpg');
// Arrow through, choose "Filter" style item
const items = await page.getByRole('menuitem').allInnerTexts();
const fIdx = items.findIndex(t => /filter/i.test(t));
for (let i = 0; i < fIdx; i++) await page.keyboard.press('ArrowDown');
await page.keyboard.press('Enter'); await page.waitForTimeout(700);
log('C after filter item: focus', await focus(page), 'dialog', (await page.locator('[role=dialog], [role=menu], .popover, form').last().innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 300));
await shot(page, '05-header-filter.jpg');
await page.keyboard.press('Escape'); await page.waitForTimeout(300); await page.keyboard.press('Escape'); await page.waitForTimeout(300);
log('D after esc: url', page.url().split('/').pop(), 'count', await count());
await browser.close();
