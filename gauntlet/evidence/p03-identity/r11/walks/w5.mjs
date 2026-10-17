import { open, signIn, focused, EV, BASE } from './h.mjs';
import fs from 'node:fs';
const log = []; const L = (s) => { log.push(s); console.log(s); };
const target = 'accountant@alnoor.example';
// attacker client: 6 wrong passwords
for (let i = 0; i < (process.env.SKIPFAIL?0:6); i++) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1', 'X-Forwarded-For': '203.0.113.9' }, body: JSON.stringify({ email: target, password: 'wrong-' + i }) });
  L(`wrong password ${i + 1}: ${r.status}${r.headers.get('retry-after') ? ' retry-after ' + r.headers.get('retry-after') : ''}`);
}
const { browser, page, errors } = await open({ width: 1600 });
await signIn(page, 'admin@alnoor.example');
await page.goto(BASE + '/identity/users'); await page.waitForTimeout(2000);
await page.keyboard.type('Hessa'); await page.waitForTimeout(2500); await page.screenshot({path:'/tmp/c-p03-r11-w5.png'}); L('rows: ' + (await page.locator('main').innerText()).slice(0,400).replace(/\n/g,' | '));
await page.locator('main [role=row]').filter({ hasText: target }).first().click(); await page.waitForTimeout(1500);
const tabs = page.locator('aside [role=tab]');
L('tabs: ' + JSON.stringify(await tabs.allInnerTexts()));
await tabs.filter({ hasText: 'Sign-in history' }).click(); await page.waitForTimeout(1200);
L('history: ' + (await page.locator('aside [role=tabpanel]').innerText()).slice(0, 600).replace(/\n/g, ' | '));
await page.screenshot({ path: `${EV}/09-sign-in-history-paused-client-en.jpg`, quality: 70, type: 'jpeg' });
const unblock = page.locator('aside button').filter({ hasText: /Allow sign-in again/ }).first();
L('unblock buttons: ' + await unblock.count());
if (await unblock.count()) { await unblock.click(); await page.waitForTimeout(1200); }
L('after unblock: ' + (await page.locator('aside').first().innerText()).slice(0, 300).replace(/\n/g, ' | '));
await tabs.filter({ hasText: 'Details' }).click(); await page.waitForTimeout(800);
const active = page.locator('aside input[type=checkbox][name=isActive], aside label:has-text("Active (can sign in)") input').first();
await active.uncheck(); await page.keyboard.press('Control+Enter'); await page.waitForTimeout(1500);
L('after untick Active + save: ' + (await page.locator('aside').first().innerText()).slice(0, 300).replace(/\n/g, ' | '));
const status = await page.evaluate(async () => (await (await fetch('/api/identity/users?search=accountant%40alnoor&take=1', { headers: { 'X-Erp-Request': '1' } })).json()).items[0].isActive);
L('server isActive after save: ' + status);
await page.screenshot({ path: `${EV}/12-unblock-then-deactivate-saved-en.jpg`, quality: 70, type: 'jpeg' });
await active.check(); await page.keyboard.press('Control+Enter'); await page.waitForTimeout(1500);
L('re-activated: ' + await page.evaluate(async () => (await (await fetch('/api/identity/users?search=accountant%40alnoor&take=1', { headers: { 'X-Erp-Request': '1' } })).json()).items[0].isActive));
// palette from another tab
await tabs.filter({ hasText: 'What they can do' }).click(); await page.waitForTimeout(1200);
L('what they can do: ' + (await page.locator('aside [role=tabpanel]').innerText()).slice(0, 500).replace(/\n/g, ' | '));
await page.screenshot({ path: `${EV}/04-what-they-can-do-en.jpg`, quality: 70, type: 'jpeg' });
await page.keyboard.press('Control+k'); await page.waitForTimeout(300); await page.keyboard.type('reset'); await page.waitForTimeout(500);
L('palette on access tab, "reset": ' + (await page.locator('[role=dialog]').first().innerText()).slice(0, 200).replace(/\n/g, ' | '));
await page.keyboard.press('Enter'); await page.waitForTimeout(800);
L('after Enter: selected tab ' + (await page.locator('aside [role=tab][aria-selected=true]').innerText()) + ' focus ' + await focused(page));
L('errors: ' + JSON.stringify(errors));
fs.writeFileSync(`${EV}/walks/w5-unblock-palette.txt`, log.join('\n'));
await browser.close();
