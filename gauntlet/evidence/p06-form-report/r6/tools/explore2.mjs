// Critic p06 r6: the reports screen by keyboard (lookup, date and choice parameters, grouping), English.
import { chromium } from '/home/shan/critic/p06-form-report-r6/gauntlet/compare/node_modules/playwright-core/index.mjs';
import { writeFileSync, appendFileSync } from 'node:fs';
const base = process.argv[2];
const R = '/home/shan/evidence-staging/p06-form-report/r6';
const log = []; const L = (...a) => { const s = a.join(' '); log.push(s); console.log(s); };
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const wait = ms => new Promise(r => setTimeout(r, ms));
const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 }, acceptDownloads: true });
const page = await ctx.newPage();
await page.goto(base + '/');
await page.locator('input[name="email"]').fill('admin@alnoor.example');
await page.locator('input[name="password"]').fill('Demo-Pass-2026');
await page.keyboard.press('Enter');
await page.locator('nav[aria-label]').first().waitFor();
const active = () => page.evaluate(() => { const e = document.activeElement; return `${e?.tagName} ${e?.getAttribute('name') || e?.getAttribute('aria-label') || e?.textContent?.trim().slice(0, 40)}`; });
await page.goto(base + '/reports/catalog'); await wait(1500);
L('reports screen focus on arrival:', await active());
// reach "Users by role" by keyboard: Tab until the focused element names it
let found = false;
for (let i = 0; i < 40; i++) { await page.keyboard.press('Tab'); const a = await active(); if (/Users by role/i.test(a)) { found = true; L('Tab presses to Users by role:', i + 1); break; } }
if (found) { await page.keyboard.press('Enter'); await wait(800); }
L('after Enter, focus:', await active(), 'url', page.url());
// first parameter (role lookup) should have focus
await page.keyboard.type('Read'); await wait(800);
L('lookup options:', JSON.stringify(await page.locator('.lookup-options li').allTextContents()));
await page.keyboard.press('Enter'); await wait(300);
L('role chosen:', await page.locator('input[name=role]').inputValue().catch(() => '?'));
const date = page.locator('input[name=signedInSince]');
L('date field present:', await date.count(), 'type', await date.getAttribute('type').catch(() => '?'));
await page.keyboard.press('Control+Enter'); await wait(2000);
const doc = page.locator('[data-testid=report-document]');
L('users by role (Read-only) document:', (await doc.textContent().catch(() => 'none'))?.replace(/\s+/g, ' ').slice(0, 500));
await page.screenshot({ path: `${R}/07-report-users-by-role-en.jpg`, type: 'jpeg', quality: 60 });
// group by another column, then download PDF and Excel from the links
const gb = page.locator('select[name=groupBy]');
L('group by options:', JSON.stringify(await gb.locator('option').allTextContents()));
await gb.selectOption({ index: 2 }).catch(e => L('groupBy select failed', e.message)); await page.keyboard.press('Control+Enter'); await wait(1500);
L('regrouped header:', (await doc.textContent().catch(() => 'none'))?.replace(/\s+/g, ' ').slice(0, 200));
const hrefs = await page.locator('.record-actions a').evaluateAll(as => as.map(a => a.getAttribute('href')));
L('download links:', JSON.stringify(hrefs));
// a bad date from the server
await page.goto(base + '/reports/catalog?report=identity.usersByRole&signedInSince=2026-13-45'); await wait(1500);
await page.keyboard.press('Control+Enter'); await wait(1500);
L('bad date ->', JSON.stringify(await page.locator('[role=alert], .field-error').allTextContents()).slice(0, 300));
writeFileSync(`${R}/browser/explore2.log`, log.join('\n'));
await browser.close();
