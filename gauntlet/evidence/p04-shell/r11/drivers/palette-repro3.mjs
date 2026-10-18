import { launch, signIn, shot, save } from './pw.mjs';
const b = await launch();
const res = {};
const state = (p) => p.evaluate(() => ({ url: location.pathname + location.search, search: document.querySelector('main input.search')?.value, heading: document.querySelector('main h2')?.textContent ?? null, rows: document.querySelectorAll('.list-row').length }));
async function palette(p, q, pick) { await p.keyboard.press('Control+k'); await p.locator('[role=dialog] input[role=combobox]').waitFor(); await p.keyboard.type(q); await p.waitForTimeout(1500); if (pick === 'last') await p.keyboard.press('End'); await p.waitForTimeout(200); const sel = await p.evaluate(() => document.querySelector('[role=option][aria-selected=true]')?.textContent.trim()); await p.keyboard.press('Enter'); await p.waitForTimeout(3000); return sel; }
const ctx = await b.newContext({ locale: 'en-US', viewport: { width: 1440, height: 900 } });
const p = await ctx.newPage();
await signIn(p, 'admin@alnoor.example');
await palette(p, 'latifa alsuw');
res.a0 = await state(p);
await p.locator('main').focus(); await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForTimeout(2500);
res.a_GU_from_record = await state(p);
res.b_sel = await palette(p, 'karim santos', 'last');
res.b_showAll = await state(p);
// back/forward
await p.goto('http://localhost:20450/'); await p.waitForTimeout(1500);
await palette(p, 'latifa alsuw'); await p.keyboard.press('Escape'); await p.waitForTimeout(800);
res.c_afterEsc = await state(p);
await p.goBack(); await p.waitForTimeout(2500);
res.c_afterBack = await state(p);
save('palette-repro3.json', res);
await b.close();
