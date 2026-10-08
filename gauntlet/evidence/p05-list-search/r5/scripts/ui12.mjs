// Critic p05 r5: End over 100,004 rows (virtualised), PageUp x50, DOM row count.
import { open, focus, shot } from './pw.mjs';
const { browser, page } = await open('admin@alnoor.example');
const log = (...a) => console.log(...a);
await page.goto('http://localhost:20550/identity/users'); await page.waitForTimeout(2000);
log('focus', await focus(page), 'DOM rows', await page.getByRole('row').count());
await page.keyboard.press('ArrowDown');
let t0 = Date.now(); await page.keyboard.press('End');
await page.waitForFunction(() => /100,004/.test(document.querySelector('[role=row][aria-rowindex="100005"], [aria-rowindex="100005"]')?.getAttribute('aria-rowindex') || '') || [...document.querySelectorAll('[role=row]')].some(r => r.getAttribute('aria-rowindex') === '100005'), null, { timeout: 15000 }).catch(e => log('rowindex wait failed'));
log('End -> last row present after', Date.now() - t0, 'ms; focus', await focus(page), 'DOM rows', await page.getByRole('row').count());
await page.waitForTimeout(800);
log('last rows', (await page.getByRole('row').allInnerTexts()).slice(-2).map(s => s.replace(/\s+/g, ' ').slice(0, 70)));
await shot(page, '03-end-of-100k-rows.jpg');
t0 = Date.now(); for (let i = 0; i < 50; i++) await page.keyboard.press('PageUp');
await page.waitForLoadState('networkidle'); log('50 PageUp settled', Date.now() - t0, 'ms; focus row', await page.evaluate(() => document.querySelector('[role=row][aria-selected=true], [role=row].active, [role=row][data-active]')?.getAttribute('aria-rowindex')));
await browser.close();
