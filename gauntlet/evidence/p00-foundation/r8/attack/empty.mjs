import { chromium } from '/home/shan/critic/p00-foundation-r8/gauntlet/compare/node_modules/playwright-core/index.mjs';
import fs from 'node:fs';
const OUT = '/home/shan/evidence-staging/p00-foundation/r8/';
const b = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US' });
const page = await ctx.newPage();
const notes = [];
await page.goto('http://localhost:20050/');
await page.waitForSelector('input[name="email"]');
await page.keyboard.type('owner@emptyco.example'); await page.keyboard.press('Enter');
await page.keyboard.type('Empty-Pass-2026x');
const t0 = Date.now(); await page.keyboard.press('Enter');
await page.waitForSelector('#navpane', { state: 'attached', timeout: 20000 });
await page.waitForTimeout(600);
notes.push(`empty workspace: signed in by keyboard in ${Date.now() - t0} ms; dir/lang ${await page.evaluate(() => document.documentElement.dir + '/' + document.documentElement.lang)}`);
notes.push('main: ' + (await page.locator('main').innerText()).slice(0, 300).replace(/\n/g, ' / '));
await page.screenshot({ path: OUT + '09-empty-workspace-first-sign-in.jpg', type: 'jpeg', quality: 65 });
// lockout message: 8 wrong passwords for a bulk user, then the right one
const ctx2 = await b.newContext({ locale: 'en-US' }); const p2 = await ctx2.newPage();
for (let i = 0; i < 8; i++) {
  await p2.goto('http://localhost:20050/'); await p2.waitForSelector('input[name="email"]');
  await p2.keyboard.type('noaccess@alnoor.example'); await p2.keyboard.press('Enter'); await p2.keyboard.type('Wrong-' + i); await p2.keyboard.press('Enter'); await p2.waitForTimeout(700);
}
await p2.goto('http://localhost:20050/'); await p2.waitForSelector('input[name="email"]');
await p2.keyboard.type('noaccess@alnoor.example'); await p2.keyboard.press('Enter'); await p2.keyboard.type('Demo-Pass-2026'); await p2.keyboard.press('Enter'); await p2.waitForTimeout(1500);
notes.push('after 8 failures, right password: ' + (await p2.locator('[role="alert"]').allInnerTexts()).join(' | ') + ' url ' + p2.url());
fs.appendFileSync(OUT + 'browser-notes.txt', notes.join('\n') + '\n');
console.log(notes.join('\n'));
await b.close();
