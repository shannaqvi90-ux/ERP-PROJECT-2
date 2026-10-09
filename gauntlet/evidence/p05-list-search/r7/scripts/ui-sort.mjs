import { open, shot } from './pw.mjs';
const base = process.argv[2], out = process.argv[3];
const { browser, page } = await open(base, 'admin@alnoor.example', { width: 1600, height: 900 });
const active = () => page.evaluate(() => { const e = document.activeElement; return `${e?.tagName}${e?.getAttribute('role') ? '[' + e.getAttribute('role') + ']' : ''} ${(e?.getAttribute('aria-label') || e?.textContent || '').slice(0, 50).replace(/\s+/g, ' ')}`; });
await page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first().click();
await page.getByRole('row').nth(5).waitFor();
for (let i = 0; i < 12; i++) { await page.keyboard.press('Tab'); const a = await active(); console.log('Tab', i + 1, a); if (/^(TH|BUTTON).*E-mail/.test(a) || /columnheader.*E-mail/.test(a)) break; }
await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
console.log('after Enter url:', page.url(), '| first row:', (await page.getByRole('row').nth(1).innerText()).replace(/\s+/g, ' ').slice(0, 80));
await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
console.log('after 2nd Enter url:', page.url(), '| first row:', (await page.getByRole('row').nth(1).innerText()).replace(/\s+/g, ' ').slice(0, 80));
// Filter from header: language menu by keyboard
await page.keyboard.press('Tab'); console.log('next tab', await active());
await page.keyboard.press('Enter'); await page.waitForTimeout(400); console.log('menu open, focus', await active());
await page.keyboard.press('Enter'); await page.waitForTimeout(600); console.log('after Enter in menu, focus', await active());
await shot(page, out + '/07-header-filter-editor.jpg');
console.log('dialog text:', (await page.locator('[role="dialog"]').first().innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 200));
await browser.close();
