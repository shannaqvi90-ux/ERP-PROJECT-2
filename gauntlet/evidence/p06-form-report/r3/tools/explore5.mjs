import fs from 'node:fs';
import { open, B, OUT, shot } from './common.mjs';
const log = []; const L = (...a) => { const s = a.join(' '); log.push(s); console.log(s); };
const { browser, page, writes } = await open('admin@alnoor.example');
const reqs = []; page.on('request', r => { if (r.url().includes('/api/reports/')) reqs.push(r.url().replace(B, '')); });
await page.goto(B + '/reports/catalog'); await page.waitForTimeout(1500);
L('focus at start', await page.evaluate(() => document.activeElement?.outerHTML.slice(0, 200)));
// keyboard: tab through to role summary
await page.getByRole('link', { name: /Users by role/ }).or(page.getByRole('button', { name: /Users by role/ })).first().click();
await page.waitForTimeout(1200);
L('users by role form', (await page.locator('main').innerText()).replace(/\n/g, ' | ').slice(0, 1500));
await page.screenshot({ path: `${OUT}/browser/report-usersbyrole-en.png`, fullPage: false });
L('report requests', JSON.stringify(reqs));
// lookup param: role
const lookup = page.locator('[data-field="role"] input');
if (await lookup.count()) {
  await lookup.click(); await page.keyboard.type('Comp'); await page.waitForTimeout(1000);
  L('lookup options', JSON.stringify(await page.getByRole('option').allInnerTexts()));
  await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(300);
  L('lookup value', await lookup.inputValue());
}
// run: find a run button
const run = page.getByRole('button', { name: /Run|Show|View/ }).first();
L('run button', await run.innerText().catch(() => 'none'));
await run.click().catch(() => {}); await page.waitForTimeout(2000);
L('after run', (await page.locator('main').innerText()).replace(/\n/g, ' | ').slice(0, 2500));
await shot(page, '06-report-users-by-role-company-manager-en');
L('report requests', JSON.stringify(reqs));
fs.writeFileSync(`${OUT}/browser/explore-reports-en.log`, log.join('\n'));
await browser.close();
