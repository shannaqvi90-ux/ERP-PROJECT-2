// Critic p05 r5: filter via header by keyboard, group, columns, save views, and/or (English).
import { open, focus, shot } from './pw.mjs';
const { browser, page } = await open('admin@alnoor.example');
const log = (...a) => console.log(...a);
const main = () => page.locator('main').innerText();
const count = async () => ((await main()).match(/(\d[\d,]*) users|one user|No users/) || ['?'])[0];
const tabTo = async (re, max = 40) => { for (let i = 0; i < max; i++) { if (re.test(await focus(page))) return true; await page.keyboard.press('Tab'); } return false; };
await page.goto('http://localhost:20550/identity/users'); await page.waitForTimeout(2000);
await tabTo(/Options for the column Language/); await page.keyboard.press('Enter'); await page.waitForTimeout(400);
await page.keyboard.press('Enter'); await page.waitForTimeout(500); // Filter…
log('1 focus', await focus(page));
// Tab to 'Arabic' checkbox and check it
await page.keyboard.press('Tab'); log('  tab ->', await focus(page));
await page.keyboard.press('Space'); await page.waitForTimeout(200);
await tabTo(/button "Apply"/, 6); await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
log('2 filtered: url', decodeURIComponent(page.url().split('/').pop()), 'count', await count(), 'chips', (await page.locator('main').getByRole('button').allInnerTexts()).filter(t => /Language|Arabic|English|×/.test(t)).join(' | '));
await shot(page, '06-filtered-language.jpg');
// Group by Status via column menu
await page.keyboard.press('Shift+Tab');
await tabTo(/Options for the column Status/); await page.keyboard.press('Enter'); await page.waitForTimeout(300);
const items = await page.getByRole('menuitem').allInnerTexts(); const g = items.findIndex(t => /Group/i.test(t));
for (let i = 0; i < g; i++) await page.keyboard.press('ArrowDown');
await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
log('3 grouped: url', decodeURIComponent(page.url().split('/').pop()), 'group rows', (await page.getByRole('row').allInnerTexts()).filter(t => /Active|Inactive/.test(t) && /\d/.test(t)).slice(0, 4).map(s => s.replace(/\s+/g, ' ').slice(0, 60)));
await shot(page, '07-grouped-status.jpg');
// Columns chooser
await page.keyboard.press('Escape');
await page.locator('input').first().focus();
await tabTo(/button "Columns"/); await page.keyboard.press('Enter'); await page.waitForTimeout(400);
log('4 columns chooser focus', await focus(page), 'content', (await page.locator('[role=dialog], [role=menu]').last().innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 200));
await page.keyboard.press('Escape'); await page.waitForTimeout(300);
// Views menu
await page.locator('input').first().focus();
await tabTo(/button "View:/); await page.keyboard.press('Enter'); await page.waitForTimeout(400);
log('5 view menu focus', await focus(page), 'content', (await page.locator('[role=dialog], [role=menu]').last().innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 300));
await shot(page, '08-views-menu.jpg');
await browser.close();
