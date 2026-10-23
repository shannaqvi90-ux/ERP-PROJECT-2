// Critic p04 r13 (from r12 critic): help sheet content in EN and AR, focus after close, Alt+L, axe-like basics
import { launch, signIn, save, shot } from './pw.mjs';
const b = await launch();
const res = {};
for (const [lang, email] of [['en', 'admin@alnoor.example'], ['ar', 'admin.ar@alnoor.example']]) {
  const ctx = await b.newContext({ locale: lang === 'ar' ? 'ar-AE' : 'en-US', viewport: { width: 1440, height: 900 } });
  const p = await ctx.newPage();
  await signIn(p, email);
  await p.keyboard.press('g'); await p.keyboard.press('u'); await p.locator('.list-row').first().waitFor();
  await p.keyboard.press('Control+/'); await p.waitForTimeout(600);
  res[lang] = await p.evaluate(() => { const d = document.querySelector('[role=dialog]'); return d ? d.innerText : null; });
  if (lang === 'ar') await shot(p, '05-help-sheet-ar.jpg');
  await p.keyboard.press('Escape'); await p.waitForTimeout(300);
  res[lang + '_focusAfter'] = await p.evaluate(() => document.activeElement?.tagName + '#' + (document.activeElement?.id || document.activeElement?.className));
  await ctx.close();
}
save('help12.json', res);
await b.close();
