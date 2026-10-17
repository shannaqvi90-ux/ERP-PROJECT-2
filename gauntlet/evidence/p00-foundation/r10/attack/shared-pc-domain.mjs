// Critic p00 r10 reuse of: Critic p00 r9: a shared counter PC. Person 1 signs in with a password on the plain address and signs
// out with the Sign out button; person 2 (same company domain) arrives at the plain address.
import { chromium } from '/home/shan/critic/p00-foundation-r10/gauntlet/compare/node_modules/playwright-core/index.mjs';
import fs from 'node:fs';
const BASE = process.env.BASE ?? 'http://localhost:20050';
const OUT = '/home/shan/evidence-staging/p00-foundation/r10/';
const b = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const notes = []; const note = (...a) => { const s = a.join(' '); notes.push(s); console.log(s); };
const ctx = await b.newContext({ locale: 'en-US', viewport: { width: 1280, height: 800 } });
const page = await ctx.newPage();
const focus = () => page.evaluate(() => document.activeElement?.getAttribute('name') ?? document.activeElement?.tagName);
await page.goto(BASE + '/'); await page.waitForSelector('input[name="email"]');
await page.keyboard.type('viewer@alnoor.example'); note('person 1 after whole e-mail (fresh device), focus:', await focus());
await page.keyboard.press('Enter'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.waitForSelector('#navpane', { state: 'attached', timeout: 20000 });
note('person 1 signed in; localStorage keys:', JSON.stringify(await page.evaluate(() => Object.keys(localStorage))));
note('erp.signInDomains:', await page.evaluate(() => localStorage.getItem('erp.signInDomains')));
await page.getByRole('button', { name: 'Sign out' }).click();
await page.waitForSelector('input[name="email"]');
note('after Sign out: e-mail field value:', JSON.stringify(await page.locator('input[name="email"]').inputValue()), 'focus:', await focus());
const hint = await page.locator('form').innerText(); note('sign-in form text after sign out:', hint.replace(/\n/g, ' / '));
// person 2, same domain
const t0 = Date.now();
await page.keyboard.type('noaccess@alnoor.example');
await page.waitForTimeout(200);
note('person 2 after typing whole e-mail, focus:', await focus());
if ((await focus()) === 'email') await page.keyboard.press('Enter');
await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.waitForSelector('#navpane', { state: 'attached', timeout: 20000 });
note('person 2 signed in after', Date.now() - t0, 'ms');
await page.getByRole('button', { name: 'Sign out' }).click();
await page.waitForSelector('input[name="email"]');
// person 3, other domain on the same PC
await page.keyboard.type('admin@gulfsteel.example'); await page.waitForTimeout(200);
note('person 3 (other workspace, never signed in here) after whole e-mail, focus:', await focus());
// Typo trap: a prefix that is itself a whole address in the known domain, e.g. "a@alnoor.example" while typing "a@alnoor.example.com"? and short user ids
await page.locator('input[name="email"]').fill('');
await page.locator('input[name="email"]').focus();
await page.keyboard.type('mariam@alnoor.example'); await page.waitForTimeout(200);
note('person 4 typing mariam@alnoor.example, focus:', await focus(), 'value:', await page.locator('input[name="email"]').inputValue());
await page.screenshot({ path: OUT + '07-shared-pc-known-domain.jpg', type: 'jpeg', quality: 60 });
fs.writeFileSync(OUT + 'attack/shared-pc-domain.txt', notes.join('\n') + '\n');
await b.close();
