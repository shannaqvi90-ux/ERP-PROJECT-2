import { launch, signIn, shot, save } from './pw.mjs';
const b = await launch();
const res = {};
const state = (p) => p.evaluate(() => ({ url: location.pathname + location.search, search: document.querySelector('main input.search')?.value, heading: document.querySelector('main h2')?.textContent }));
async function palette(p, q) { await p.keyboard.press('Control+k'); await p.locator('[role=dialog] input[role=combobox]').waitFor(); await p.keyboard.type(q); await p.waitForTimeout(1500); const opts = await p.evaluate(() => [...document.querySelectorAll('[role=option]')].map(o => o.textContent.trim()).slice(0, 6)); const first = await p.evaluate(() => document.querySelector('[role=option][aria-selected=true]')?.textContent.trim()); await p.keyboard.press('Enter'); await p.waitForTimeout(3000); return { first, opts }; }
const ctx = await b.newContext({ locale: 'en-US', viewport: { width: 1440, height: 900 } });
const p = await ctx.newPage();
const puts = [];
p.on('request', r => { if (r.method() === 'PUT') puts.push(`${r.method()} ${new URL(r.url()).pathname}`); });
await signIn(p, 'admin@alnoor.example');
// roles
res.role1 = { ...(await palette(p, 'Read-only')), ...(await state(p)) };
res.role2 = { ...(await palette(p, 'Staff')), ...(await state(p)) };
// companies
res.co1 = { ...(await palette(p, 'ALN-SHJ')), ...(await state(p)) };
res.co2 = { ...(await palette(p, 'ALN-AUH')), ...(await state(p)) };
// users: then save
res.u1 = { ...(await palette(p, 'latifa alsuw')), ...(await state(p)) };
res.u2 = { ...(await palette(p, 'Mariam Al Mansoori')), ...(await state(p)) };
await shot(p, '11-palette-second-record-stale-en.jpg');
const nm = p.locator('input[name=displayName]');
if (await nm.count()) { await nm.focus(); await p.keyboard.press('End'); await p.keyboard.type('.'); await p.keyboard.press('Control+s'); await p.waitForTimeout(1500); res.putsOnSave = [...puts];
  await nm.focus(); await p.keyboard.press('End'); await p.keyboard.press('Backspace'); await p.keyboard.press('Control+s'); await p.waitForTimeout(1500); }
await p.reload(); await p.waitForTimeout(3000);
res.afterReload = await state(p);
save('palette-repro2.json', res);
await b.close();
