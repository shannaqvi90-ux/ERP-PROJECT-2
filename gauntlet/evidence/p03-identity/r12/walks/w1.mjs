// Walk 1 (EN, keyboard): sign in, users list, find one user, create a Read-only user by keyboard.
import { open, signIn, focused, EV, shot } from './h.mjs';
import fs from 'node:fs';
const log = []; const L = (s) => { log.push(s); console.log(s); };
const { browser, page, errors } = await open();
await signIn(page, 'admin@alnoor.example');
L('after sign in url ' + page.url() + ' dir=' + await page.locator('html').getAttribute('dir'));
await page.keyboard.press('g'); await page.keyboard.press('u'); await page.waitForTimeout(2500);
L('users url ' + page.url());
const main = await page.locator('main').innerText();
L('users list head: ' + main.slice(0, 500).replace(/\n/g, ' | '));
// find one user: needle from the shared dataset
const needles = JSON.parse(fs.readFileSync('/home/shan/critic/p03-identity-r12/gauntlet/compare/data/out/needles.json', 'utf8'));
L('needles: ' + JSON.stringify(needles).slice(0, 300));
const needle = needles.user?.displayName || needles.users?.[0]?.displayName || needles.user?.name;
L('focus before typing: ' + await focused(page));
// focus is already in the search box on arrival
L('after / focus: ' + await focused(page));
const t0 = Date.now();
await page.keyboard.type(String(needle)); await page.keyboard.press('Enter');
await page.waitForFunction(() => /\b1\b/.test(document.querySelector('main')?.innerText || ''), null, { timeout: 15000 }).catch(() => {});
await page.waitForTimeout(800);
L(`search "${needle}" took ~${Date.now() - t0} ms; list now: ` + (await page.locator('main').innerText()).slice(0, 400).replace(/\n/g, ' | '));
await shot(page, '01-find-one-user-among-100000-en');
// create a read-only user by keyboard
const stamp = Date.now().toString(36);
await page.keyboard.press('Escape');
await page.keyboard.press('Alt+n'); await page.waitForTimeout(800);
L('after Alt+N focus: ' + await focused(page));
await page.keyboard.type(`clerk.${stamp}@alnoor.example`);
await page.keyboard.press('Alt+o'); await page.waitForTimeout(300);
L('after Alt+O focus: ' + await focused(page));
await page.keyboard.type('read'); await page.keyboard.press('Enter'); await page.waitForTimeout(300);
L('checked roles: ' + JSON.stringify(await page.locator('aside input[type=checkbox]:checked').evaluateAll((els) => els.map((e) => e.closest('label')?.innerText))));
await page.keyboard.press('Control+Enter'); await page.waitForTimeout(1800);
const aside = (await page.locator('aside').first().innerText());
L('after save aside: ' + aside.slice(0, 700).replace(/\n/g, ' | '));
const code = (aside.match(/[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}/) || [])[0];
L('set-up code shown: ' + code);
await shot(page, '02-new-read-only-user-set-up-code-en');
fs.writeFileSync(EV + '/walks/w1.out.json', JSON.stringify({ email: `clerk.${stamp}@alnoor.example`, code }));
L('errors: ' + JSON.stringify(errors));
fs.writeFileSync(EV + '/walks/w1.txt', log.join('\n'));
await browser.close();
