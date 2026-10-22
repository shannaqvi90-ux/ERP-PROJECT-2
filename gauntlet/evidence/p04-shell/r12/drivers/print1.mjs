import { launch, signIn, shot, save, OUT } from './pw.mjs';
const b = await launch();
const res = {};
const ctx = await b.newContext({ locale: 'ar-AE', viewport: { width: 1440, height: 900 } });
const p = await ctx.newPage();
await signIn(p, 'admin.ar@alnoor.example');
await p.keyboard.press('g'); await p.keyboard.press('u');
await p.waitForURL('**/identity/users');
await p.locator('.list-row').first().waitFor();
const probe = () => p.evaluate(() => {
  const a = document.activeElement; const cs = getComputedStyle(a); const before = getComputedStyle(a, '::before');
  return { el: a.outerHTML.slice(0, 120), outline: `${cs.outlineStyle} ${cs.outlineWidth} ${cs.outlineColor}`, boxShadow: cs.boxShadow, bg: cs.backgroundColor, border: cs.borderColor, beforeBg: before.backgroundColor, beforeContent: before.content, caret: cs.caretColor };
});
const results = [];
// cycle through tab stops, probing each in screen and print
for (let i = 0; i < 30; i++) {
  await p.keyboard.press('Tab');
  await p.emulateMedia({ media: 'screen' });
  const s = await probe();
  await p.emulateMedia({ media: 'print' });
  const pr = await probe();
  await p.emulateMedia({ media: 'screen' });
  results.push({ i, screen: s, print: pr });
}
res.tabStops = results.map(r => ({ i: r.i, el: r.screen.el.slice(0, 80), screenOutline: r.screen.outline, printOutline: r.print.outline, printShadow: r.print.boxShadow, printBg: r.print.bg, printBorder: r.print.border, printBefore: r.print.beforeBg }));
res.printLeaks = results.filter(r => !/none|0px/.test(r.print.outline) || (r.print.boxShadow !== 'none') || (r.screen.beforeBg !== r.print.beforeBg && r.print.beforeBg !== 'rgba(0, 0, 0, 0)')).map(r => ({ i: r.i, el: r.print.el, print: r.print }));
save('print1.json', res);
await b.close();
