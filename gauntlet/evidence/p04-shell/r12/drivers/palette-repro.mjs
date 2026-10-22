import { launch, signIn, shot, save } from './pw.mjs';
const b = await launch();
const res = {};
const state = (p) => p.evaluate(() => ({ url: location.pathname + location.search, search: document.querySelector('main input.search')?.value, rows: document.querySelectorAll('.list-row').length, panelName: document.querySelector('input[name=displayName]')?.value ?? null, heading: document.querySelector('main h2')?.textContent }));
async function palette(p, q) { await p.keyboard.press('Control+k'); await p.locator('[role=dialog] input[role=combobox]').waitFor(); await p.keyboard.type(q); await p.waitForTimeout(1500); const first = await p.evaluate(() => document.querySelector('[role=option][aria-selected=true]')?.textContent.trim()); await p.keyboard.press('Enter'); await p.waitForTimeout(3000); return first; }
for (const variant of ['withEscape', 'noEscape', 'arabic']) {
  const ctx = await b.newContext({ locale: 'en-US', viewport: { width: 1440, height: 900 } });
  const p = await ctx.newPage();
  await signIn(p, variant === 'arabic' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example');
  const r = { steps: [] };
  r.steps.push({ chose: await palette(p, 'latifa alsuw'), ...(await state(p)) });
  if (variant === 'withEscape') { await p.keyboard.press('Escape'); await p.waitForTimeout(800); r.steps.push({ after: 'Escape', ...(await state(p)) }); }
  r.steps.push({ chose: await palette(p, 'Mariam Al Mansoori'), ...(await state(p)) });
  await shot(p, `dbg-palette-${variant}.jpg`);
  await p.waitForTimeout(4000);
  r.steps.push({ after: '+4s', ...(await state(p)) });
  res[variant] = r;
  await ctx.close();
}
save('palette-repro.json', res);
await b.close();
