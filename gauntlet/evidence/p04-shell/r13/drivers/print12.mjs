// Critic p04 r13 (from r12 critic): A4 print of the users list and of an open record, EN and AR.
import { launch, signIn, save, OUT } from './pw.mjs';
const b = await launch();
const res = {};
const measure = (p) => p.evaluate(() => {
  const vis = (el) => { const s = getComputedStyle(el); return s.display !== 'none' && s.visibility !== 'hidden' && el.getClientRects().length > 0; };
  const grid = document.querySelector('.list-grid');
  const ths = [...document.querySelectorAll('.list-grid thead th, .list-grid [role=columnheader]')].filter(vis).map(th => { const r = th.getBoundingClientRect(); return { text: th.innerText.replace(/\n|▾|▴/g, '').trim(), left: Math.round(r.left), right: Math.round(r.right) }; });
  const offPage = ths.filter(t => t.left < -1 || t.right > innerWidth + 1);
  const cells = [...document.querySelectorAll('.list-grid td, .list-grid [role=gridcell]')].filter(vis);
  const clipped = cells.filter(c => c.scrollWidth > c.clientWidth + 1 || [...c.querySelectorAll('*')].some(x => x.scrollWidth > x.clientWidth + 1 && getComputedStyle(x).overflow !== 'visible')).slice(0, 8).map(c => ({ text: c.innerText.trim(), sw: c.scrollWidth, cw: c.clientWidth }));
  const anyWide = [...document.querySelectorAll('main *')].filter(vis).filter(e => { const r = e.getBoundingClientRect(); return r.width > 0 && (r.right > innerWidth + 2 || r.left < -2); }).slice(0, 8).map(e => ({ tag: e.tagName, cls: e.className?.toString().slice(0, 40), text: e.innerText?.trim().slice(0, 40), left: Math.round(e.getBoundingClientRect().left), right: Math.round(e.getBoundingClientRect().right) }));
  const rec = document.querySelector('input[name=displayName]');
  return { dir: document.documentElement.dir, vw: innerWidth, gridW: grid ? Math.round(grid.getBoundingClientRect().width) : null, gridScrollW: grid?.scrollWidth, ths, offPage, clipped, anyWide, recordVisible: rec ? vis(rec) : null, docH: document.documentElement.scrollHeight };
});
for (const [lang, email] of [['en', 'admin@alnoor.example'], ['ar', 'admin.ar@alnoor.example']]) {
  const ctx = await b.newContext({ locale: lang === 'ar' ? 'ar-AE' : 'en-US', viewport: { width: 1440, height: 900 } });
  const p = await ctx.newPage();
  await signIn(p, email);
  await p.keyboard.press('g'); await p.keyboard.press('u');
  await p.waitForURL('**/identity/users');
  await p.locator('.list-row').first().waitFor();
  await p.waitForTimeout(500);
  await p.setViewportSize({ width: 718, height: 1000 });
  await p.emulateMedia({ media: 'print' });
  await p.waitForTimeout(400);
  res[lang + '_list'] = await measure(p);
  await p.pdf({ path: `${OUT}/print-users-${lang}-a4.pdf`, format: 'A4' });
  await p.screenshot({ path: `${OUT}/dbg-print-a4-users-${lang}.jpg`, type: 'jpeg', quality: 70 });
  await p.emulateMedia({ media: 'screen' }); await p.setViewportSize({ width: 1440, height: 900 });
  // open the third row's record
  await p.locator('.list-row').nth(2).click(); await p.waitForTimeout(1500);
  await p.setViewportSize({ width: 718, height: 1000 });
  await p.emulateMedia({ media: 'print' }); await p.waitForTimeout(400);
  res[lang + '_record'] = await measure(p);
  await p.pdf({ path: `${OUT}/print-user-record-${lang}-a4.pdf`, format: 'A4' });
  await p.screenshot({ path: `${OUT}/dbg-print-a4-record-${lang}.jpg`, type: 'jpeg', quality: 70, fullPage: true });
  await ctx.close();
}
save('print12.json', res);
await b.close();
