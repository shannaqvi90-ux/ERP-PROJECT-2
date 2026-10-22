// Critic p04 r12: where does an open record go on paper? (print media, A4 width)
import { launch, signIn, save, OUT } from './pw.mjs';
const b = await launch();
const res = {};
for (const [lang, email] of [['en', 'admin@alnoor.example'], ['ar', 'admin.ar@alnoor.example']]) {
  const ctx = await b.newContext({ locale: lang === 'ar' ? 'ar-AE' : 'en-US', viewport: { width: 1440, height: 900 } });
  const p = await ctx.newPage();
  await signIn(p, email);
  await p.keyboard.press('g'); await p.keyboard.press('u');
  await p.locator('.list-row').first().waitFor(); await p.waitForTimeout(500);
  await p.locator('.list-row').nth(2).click(); await p.waitForTimeout(1500);
  await p.setViewportSize({ width: 718, height: 1000 });
  await p.emulateMedia({ media: 'print' }); await p.waitForTimeout(400);
  res[lang] = await p.evaluate(() => {
    const r = (el) => { if (!el) return null; const b = el.getBoundingClientRect(); const s = getComputedStyle(el); return { top: Math.round(b.top + scrollY), left: Math.round(b.left), w: Math.round(b.width), h: Math.round(b.height), display: s.display, vis: s.visibility, pos: s.position, overflow: s.overflow }; };
    const inp = document.querySelector('input[name=displayName]');
    let panel = inp; for (let i = 0; i < 8 && panel; i++) { if (panel.matches('aside, section, [role=region], [role=dialog], form')) break; panel = panel.parentElement; }
    const list = document.querySelector('.list-grid');
    return { input: r(inp), panel: r(panel), panelTag: panel?.tagName + '.' + panel?.className, panelText: panel?.innerText.slice(0, 400), list: r(list), listRows: document.querySelectorAll('.list-row').length, docH: document.documentElement.scrollHeight, bodyText: document.body.innerText.slice(0, 300) };
  });
  await p.pdf({ path: `${OUT}/print-user-record-${lang}-a4.pdf`, format: 'A4' });
  await ctx.close();
}
save('print12b.json', res);
await b.close();
