import { launch, signIn, shot, save } from './pw.mjs';
const b = await launch();
const res = {};
const ctx = await b.newContext({ locale: 'en-US', viewport: { width: 1440, height: 900 } });
const p = await ctx.newPage();
const writes = [];
p.on('response', r => { if (r.request().method() !== 'GET') writes.push(`${r.request().method()} ${new URL(r.url()).pathname} ${r.status()}`); });
await signIn(p, 'admin@alnoor.example');
// palette: record search
await p.keyboard.press('Control+k');
await p.locator('[role=dialog] input[role=combobox]').waitFor();
await p.keyboard.type('latifa alsuw');
await p.waitForTimeout(1200);
res.paletteRecord = await p.evaluate(() => [...document.querySelectorAll('[role=option]')].map(o => o.textContent.trim()).slice(0, 8));
await shot(p, '09-palette-record-en.jpg');
await p.keyboard.press('Enter');
await p.waitForTimeout(1500);
res.afterPaletteRecord = { url: p.url(), focus: await p.evaluate(() => document.activeElement?.outerHTML.slice(0, 150)) };
await p.keyboard.press('Escape'); await p.waitForTimeout(300);
// Own record dirty + Alt+L
await p.keyboard.press('Control+k'); await p.keyboard.type('Mariam Al Mansoori'); await p.waitForTimeout(1200);
res.paletteOwn = await p.evaluate(() => [...document.querySelectorAll('[role=option]')].map(o => o.textContent.trim()).slice(0, 5));
await p.keyboard.press('Enter'); await p.waitForTimeout(1500);
await p.waitForTimeout(3000); res.ownUrl = p.url(); res.fields = await p.evaluate(() => [...document.querySelectorAll('input,select')].map(i => i.name + ':' + (i.closest('main')?'M':'-')).filter(Boolean)); save('explore3.json', res); await shot(p, 'dbg-own.jpg');
const name = p.locator('input[name="displayName"]').first();
await name.focus(); await p.keyboard.press('End'); await p.keyboard.type(' Q');
await p.keyboard.press('Alt+l');
await p.waitForFunction(() => document.documentElement.dir === 'rtl');
await p.waitForTimeout(1500);
res.dirtyAfterSwitch = await p.evaluate(() => ({ name: document.querySelector('main input[name=name]')?.value, lang: document.querySelector('select[name=language]')?.value, focus: document.activeElement?.getAttribute('name') }));
await p.keyboard.press('Control+s'); await p.waitForTimeout(1500);
res.afterSaveMsg = await p.evaluate(() => [...document.querySelectorAll('[role=status],[role=alert],.form-message,.notice')].map(e => e.textContent.trim()).filter(Boolean).slice(0, 5));
res.serverAfter = await p.evaluate(async (u) => { const id = u.split('/').pop(); const r = await fetch('/api/identity/users/' + id, { headers: { 'X-Erp-Request': '1' } }); const j = await r.json(); return { displayName: j.displayName, language: j.language }; }, p.url());
// revert
await name.focus(); await p.keyboard.press('End'); await p.keyboard.press('Backspace'); await p.keyboard.press('Backspace');
await p.keyboard.press('Control+s'); await p.waitForTimeout(1200);
await p.keyboard.press('Alt+l'); await p.waitForFunction(() => document.documentElement.dir === 'ltr'); await p.waitForTimeout(800);
// Keyboard-only: Columns menu & Print or export from users list
await p.keyboard.press('Escape'); await p.waitForTimeout(300);
await p.keyboard.press('Alt+Shift+r'); await p.waitForTimeout(800);
res.printExportDialog = await p.evaluate(() => { const d = document.querySelector('[role=dialog],[role=menu]'); return d ? { text: d.innerText.slice(0, 400), focus: document.activeElement?.outerHTML.slice(0, 120) } : null; });
await shot(p, '10-print-export-keyboard-en.jpg');
await p.keyboard.press('Escape');
res.writes = writes;
save('explore3.json', res); await shot(p, 'dbg-own.jpg');
await b.close();
