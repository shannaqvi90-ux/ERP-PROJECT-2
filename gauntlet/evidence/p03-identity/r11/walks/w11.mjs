import { open, signIn, focused, EV, BASE } from './h.mjs';
import fs from 'node:fs';
const log = []; const L = (s) => { log.push(s); console.log(s); };
const needle = JSON.parse(fs.readFileSync('/home/shan/critic/p03-identity-r11/gauntlet/compare/data/out/needles.json', 'utf8'));
const target = needle.user ?? needle.users ?? Object.values(needle).find((v) => v && v.email);
L('needle: ' + JSON.stringify(target).slice(0, 200));
const { browser, page, errors } = await open({ width: 1600 });
await signIn(page, 'admin@alnoor.example');
await page.keyboard.press('g'); await page.keyboard.press('u'); await page.waitForTimeout(2500);
const t0 = Date.now();
await page.keyboard.type(target.name ?? target.displayName ?? target.login);
await page.locator('main [role=row]').nth(1).waitFor();
await page.waitForFunction(() => /^\s*1 users?\b|\b1 user\b/.test(document.querySelector('main')?.innerText ?? ''), null, { timeout: 10000 }).catch(() => {});
L(`typed name; list after ${Date.now() - t0} ms: ` + (await page.locator('main').innerText()).slice(0, 300).replace(/\n/g, ' | '));
await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
L('opened: ' + (await page.locator('aside').first().innerText()).slice(0, 120).replace(/\n/g, ' | '));
await page.screenshot({ path: `${EV}/01-find-one-user-among-100000-en.jpg`, quality: 70, type: 'jpeg' });
// reset password via palette
await page.keyboard.press('Control+k'); await page.waitForTimeout(300); await page.keyboard.type('reset'); await page.waitForTimeout(400); await page.keyboard.press('Enter'); await page.waitForTimeout(600);
L('reset form focus: ' + await focused(page) + ' text ' + (await page.locator('aside').first().innerText()).match(/Reset password[\s\S]{0,300}/)?.[0].replace(/\n/g, ' | '));
await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
L('after reset Enter: ' + ((await page.locator('aside').first().innerText()).match(/code[\s\S]{0,160}/i)?.[0] ?? '').replace(/\n/g, ' | '));
// deactivated user cannot sign in
const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: 'noaccess@alnoor.example', password: 'Demo-Pass-2026' }) });
L('noaccess (active, no roles) sign-in: ' + r.status);
L('errors: ' + JSON.stringify(errors));
fs.writeFileSync(`${EV}/walks/w11-find-reset.txt`, log.join('\n'));
await browser.close();
