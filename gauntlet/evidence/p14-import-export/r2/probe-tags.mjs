// Probe of the contacts list for the export-filtered-list path: how many rows the grid renders, where the first Government tag button is,
// what the Tags column menu offers, and whether the list search matches a tag. Run from gauntlet/compare of the critic clone.
import path from 'node:path';
import { createRequire } from 'node:module';
const require = createRequire(path.join(process.cwd(), 'package.json'));
const { chromium } = require('playwright-core');
const { findChromium } = await import(path.join(process.cwd(), 'lib/browser.mjs'));
const BASE = process.env.BASE || 'http://localhost:21450';
const OUT = process.env.OUT || '.';
const browser = await chromium.launch({ executablePath: findChromium(), headless: true });
const ctx = await browser.newContext({ viewport: { width: 1600, height: 900 } });
const page = await ctx.newPage();
await page.goto(BASE + '/');
await page.locator('input[name="email"]:focus').waitFor();
await page.keyboard.type('admin@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.locator('nav[aria-label="Main navigation"]').waitFor();
await page.getByRole('navigation').getByRole('link', { name: 'Contacts', exact: true }).first().click();
await page.locator('table[role=grid] tbody tr').first().waitFor();
await page.waitForTimeout(1500);
console.log('rows in DOM', await page.locator('table[role=grid] tbody tr').count(), 'tag buttons', await page.locator('button.ct-tag').count(), 'Government buttons', await page.locator('button.ct-tag', { hasText: /^Government$/ }).count());
// Scroll by keyboard: focus the grid, PageDown until a Government tag button shows.
await page.locator('table[role=grid] tbody tr').first().click({ position: { x: 5, y: 5 } }).catch(() => {});
for (let i = 1; i <= 12; i++) {
  await page.keyboard.press('PageDown'); await page.waitForTimeout(400);
  const n = await page.locator('button.ct-tag', { hasText: /^Government$/ }).count();
  const vis = n ? await page.locator('button.ct-tag', { hasText: /^Government$/ }).first().isVisible() : false;
  if (n) { const box = await page.locator('button.ct-tag', { hasText: /^Government$/ }).first().boundingBox(); console.log(`PageDown x${i}: Government buttons ${n}, first box y ${box?.y?.toFixed(0)}`); break; }
}
// Tags column menu.
await page.goto(BASE + '/contacts/contacts'); await page.locator('table[role=grid] tbody tr').first().waitFor(); await page.waitForTimeout(800);
const th = page.locator('table[role=grid] thead th', { hasText: 'Tags' });
await th.locator('button').last().click().catch(e => console.log('no menu button', e.message));
await page.waitForTimeout(500);
console.log('Tags menu items:', (await page.locator('[role=menu] [role=menuitem], [role=menu] button').allInnerTexts()).join(' | '));
await page.screenshot({ path: path.join(OUT, 'probe-tags-menu.jpg'), type: 'jpeg', quality: 55 });
await page.keyboard.press('Escape');
// List search for the tag.
await page.locator('input[type=search]').first().fill('Government'); await page.keyboard.press('Enter'); await page.waitForTimeout(2000);
console.log('search "Government":', (await page.locator('main').innerText()).match(/[\d,]+ contacts?/)?.[0]);
await browser.close();
