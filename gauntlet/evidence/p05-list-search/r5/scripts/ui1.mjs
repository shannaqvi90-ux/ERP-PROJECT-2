// Critic p05 r5: users list by keyboard in English (search, open, grid, sort, header filter, columns, end of list).
import { open, focus, shot } from './pw.mjs';
const { browser, page } = await open('admin@alnoor.example');
const log = (...a) => console.log(...a);
const count = () => page.locator('main').getByText(/\d[\d,]* users?|one user|No users/).first().innerText().catch(() => '?');
await page.keyboard.press('Control+k'); await page.keyboard.type('users'); await page.waitForTimeout(400); await page.keyboard.press('Enter');
await page.waitForTimeout(1500);
log('1 palette->users url', page.url(), 'focus', await focus(page), 'count', await count());
let t0 = Date.now();
await page.keyboard.type('Maj Ani Pil', { delay: 40 });
await page.waitForFunction(() => /\b\d[\d,]* users\b|one user/.test(document.querySelector('main').innerText) && !/100,004 users/.test(document.querySelector('main').innerText), null, { timeout: 10000 }).catch(() => {});
await page.waitForTimeout(600);
log('2 typed prefixes, count', await count(), (Date.now() - t0) + 'ms', 'first row', (await page.getByRole('row').nth(1).innerText()).replace(/\s+/g, ' '));
await shot(page, '01-search-prefixes-best-match-first.jpg');
await page.keyboard.press('Enter'); await page.waitForTimeout(1000);
log('3 Enter -> url', page.url(), 'focus', await focus(page));
const panel = await page.locator('[role="dialog"], aside, [role="complementary"]').first().innerText().catch(() => '');
log('   panel', panel.replace(/\s+/g, ' ').slice(0, 200));
await shot(page, '02-enter-opens-best-match.jpg');
await page.keyboard.press('Escape'); await page.waitForTimeout(500);
log('4 Esc -> url', page.url(), 'focus', await focus(page));
// clear search
await page.keyboard.press('Control+a'); await page.keyboard.press('Backspace'); await page.waitForTimeout(1200);
log('5 cleared, count', await count());
await page.keyboard.press('ArrowDown'); await page.waitForTimeout(200);
log('6 ArrowDown -> focus', await focus(page));
await page.keyboard.press('End'); t0 = Date.now(); await page.waitForTimeout(2500);
log('7 End -> focus', await focus(page), 'last rows', (await page.getByRole('row').allInnerTexts()).slice(-2).map(s => s.replace(/\s+/g, ' ')).join(' || '));
await shot(page, '03-end-of-100k-rows.jpg');
await page.keyboard.press('Home'); await page.waitForTimeout(1200);
log('8 Home -> focus', await focus(page));
// Shift+Tab back to headers? find column header buttons
await page.keyboard.press('Shift+Tab'); log('9 Shift+Tab ->', await focus(page));
await page.keyboard.press('Shift+Tab'); log('10 Shift+Tab ->', await focus(page));
await page.keyboard.press('Shift+Tab'); log('11 Shift+Tab ->', await focus(page));
await browser.close();
