// Critic p05 r4: bulk action on chosen rows and on all matching rows.
import { chromium } from '/home/shan/critic/p05-list-search-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
const base = 'http://localhost:20550';
const out = '/home/shan/evidence-staging/p05-list-search/r4/';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const log = (...a) => console.log(...a);
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await ctx.newPage();
page.on('dialog', d => { log('   [dialog]', d.message()); d.accept(); });
await page.goto(base + '/');
await page.getByLabel('E-mail', { exact: true }).fill('admin@alnoor.example');
await page.getByLabel('Password', { exact: true }).fill('Demo-Pass-2026');
await page.keyboard.press('Enter');
await page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first().click();
await page.waitForTimeout(1500);
await page.keyboard.type(process.argv[2] || 'rania smith', { delay: 30 });
await page.waitForTimeout(1500);
log('count', await page.locator('.list-count').innerText());
await page.keyboard.press('ArrowDown');
await page.keyboard.press('Space'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Space');
const bar = page.locator('.list-selectionbar');
log('bar', (await bar.innerText()).replace(/\s+/g, ' '));
if (!process.argv[2]) await bar.getByRole('button', { name: 'Deactivate' }).click();
await page.waitForTimeout(1500);
log('after deactivate notice', (await page.locator('main').innerText()).match(/[^\n]*(deactivat|changed|updated)[^\n]*/gi));
await page.screenshot({ path: out + 'raw-bulk.png' });
// All matching
await page.locator('[role="grid"]').focus();
await page.keyboard.press('Control+a'); await page.keyboard.press('Control+a');
log('bar all', (await bar.innerText()).replace(/\s+/g, ' '));
const act = bar.getByRole('button', { name: 'Activate', exact: true });
log('Activate on all-matching disabled?', await act.isDisabled(), 'title', await act.getAttribute('title'));
if (!(await act.isDisabled())) { await act.click(); await page.waitForTimeout(2500); log('after activate all', (await page.locator('main').innerText()).match(/[^\n]*(activat|changed|updated)[^\n]*/gi)); }
await page.screenshot({ path: out + '11-bulk-all-matching.jpg', type: 'jpeg', quality: 70 });
await browser.close();
