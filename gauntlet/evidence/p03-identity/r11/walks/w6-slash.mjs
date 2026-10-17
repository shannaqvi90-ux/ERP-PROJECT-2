import { open, signIn, focused, EV, BASE } from './h.mjs';
import fs from 'node:fs';
const log = []; const L = (s) => { log.push(s); console.log(s); };
const { browser, page } = await open({ width: 1600 });
await signIn(page, 'admin@alnoor.example');
for (const path of ['/identity/users', '/identity/roles', '/tenancy/companies']) {
  await page.goto(BASE + path); await page.waitForTimeout(2500);
  L(`${path}: focus before: ${await focused(page)}`);
  await page.keyboard.press('/'); await page.waitForTimeout(400);
  L(`${path}: after "/" focus ${await focused(page)}; value=${JSON.stringify(await page.evaluate(() => document.activeElement.value))}`);
  // via grid: click a row then '/'
  await page.keyboard.press('Escape'); await page.locator('main [role=row]').nth(1).click(); await page.waitForTimeout(500); await page.keyboard.press('Escape'); await page.waitForTimeout(300);
  await page.locator('main [role=grid]').first().focus().catch(() => {});
  L(`${path}: grid focus ${await focused(page)}`);
  await page.keyboard.press('/'); await page.waitForTimeout(400);
  L(`${path}: after "/" from grid, focus ${await focused(page)}; value=${JSON.stringify(await page.evaluate(() => document.activeElement.value))}`);
}
fs.writeFileSync(`${EV}/walks/w6-slash-key.txt`, log.join('\n'));
await browser.close();
