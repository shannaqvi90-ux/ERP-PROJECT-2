import { launch, signIn, shot, save, BASE } from './pw.mjs';
const b = await launch();
const res = {};
const ctx = await b.newContext({ locale: 'en-US', viewport: { width: 1440, height: 900 } });
const p = await ctx.newPage();
const puts = [];
p.on('response', r => { if (r.request().method() !== 'GET') puts.push(`${r.request().method()} ${new URL(r.url()).pathname} ${r.status()}`); });
await signIn(p, 'admin@alnoor.example');
await p.keyboard.press('g'); await p.keyboard.press('u');
await p.waitForURL('**/identity/users');
await p.locator('.list-row').first().waitFor();
res.focusOnArrival = await p.evaluate(() => document.activeElement?.className);
await p.waitForTimeout(150);
res.searchFocus = await p.evaluate(() => document.activeElement?.outerHTML.slice(0, 200));
await p.keyboard.type('admin@alnoor.example');
await p.waitForTimeout(1500);
const ownRow = () => p.evaluate(() => [...document.querySelectorAll('.list-row')].map(r => r.innerText.replace(/\n/g, ' | ')).slice(0, 4));
res.ownRowBefore = await ownRow();
await shot(p, '03-users-own-row-en.jpg');
// Alt+L
const t0 = Date.now();
await p.keyboard.press('Alt+l');
await p.waitForFunction(() => document.documentElement.dir === 'rtl');
res.switchMs = Date.now() - t0;
await p.waitForTimeout(1500);
res.ownRowAfter = await ownRow();
res.dirAfter = await p.evaluate(() => document.documentElement.dir);
await shot(p, '04-users-own-row-after-altl-ar.jpg');
// open own row
await p.keyboard.press('ArrowDown');
await p.keyboard.press('Enter');
await p.waitForTimeout(1500);
res.panel = await p.evaluate(() => { const f = document.querySelector('form, .record-panel, [role=region]'); return document.querySelector('main').innerText.slice(0, 1500); });
// switch back to English with record open
await p.keyboard.press('Alt+l');
await p.waitForFunction(() => document.documentElement.dir === 'ltr');
await p.waitForTimeout(1500);
res.panelAfterEn = await p.evaluate(() => { const sel = document.querySelector('select[name="language"], [name="language"]'); return { lang: sel ? (sel.value || sel.textContent) : null, html: sel?.outerHTML.slice(0, 300) }; });
res.ownRowAfterEn = await ownRow();
await shot(p, '05-own-record-after-switch-en.jpg');
// edit display name in own record and save with Ctrl+S to see no conflict
const nameField = p.locator('input[name="displayName"], input[name="nameEn"], input[name="name"]').first();
res.nameFieldCount = await nameField.count();
if (res.nameFieldCount) {
  await nameField.focus();
  await p.keyboard.press('End');
  await p.keyboard.type(' X');
  await p.keyboard.press('Control+s');
  await p.waitForTimeout(1500);
  res.afterSave = await p.evaluate(() => document.querySelector('main').innerText.slice(0, 600));
  // revert
  await nameField.focus(); await p.keyboard.press('End'); await p.keyboard.press('Backspace'); await p.keyboard.press('Backspace');
  await p.keyboard.press('Control+s'); await p.waitForTimeout(1500);
}
res.writes = puts;
save('explore2.json', res);
await b.close();
