import { launch, signIn, save, OUT } from './pw.mjs';
const b = await launch();
const res = {};
for (const [lang, email] of [['en', 'admin@alnoor.example'], ['ar', 'admin.ar@alnoor.example']]) {
  const ctx = await b.newContext({ locale: lang === 'ar' ? 'ar-AE' : 'en-US', viewport: { width: 1440, height: 900 } });
  const p = await ctx.newPage();
  await signIn(p, email);
  await p.keyboard.press('g'); await p.keyboard.press('u');
  await p.waitForURL('**/identity/users');
  await p.locator('.list-row').first().waitFor();
  // palette: print this screen
  await p.keyboard.press('Control+k');
  await p.locator('[role=dialog] input[role=combobox]').waitFor();
  await p.keyboard.type(lang === 'ar' ? 'طباعة' : 'print');
  await p.waitForTimeout(500);
  res[lang + 'PaletteOptions'] = await p.evaluate(() => [...document.querySelectorAll('[role=option]')].map(o => o.textContent.trim()).slice(0, 8));
  await p.keyboard.press('Escape');
  // measure at A4 printable width (Chrome default margins ~ 0.4in each side => 794 - 2*38 = 718 px)
  await p.setViewportSize({ width: 718, height: 1000 });
  await p.emulateMedia({ media: 'print' });
  await p.waitForTimeout(300);
  res[lang] = await p.evaluate(() => {
    const doc = document.querySelector('main article') || document.querySelector('main');
    const pr = doc.getBoundingClientRect();
    const ths = [...document.querySelectorAll('.list-grid thead th, .list-grid [role=columnheader]')].map(th => { const r = th.getBoundingClientRect(); return { text: th.innerText.replace(/\n|▾/g, '').trim(), left: Math.round(r.left), right: Math.round(r.right) }; });
    const grid = document.querySelector('.list-grid'); const sc = grid?.closest('.list-scroll') || grid?.parentElement;
    return { dir: document.documentElement.dir, docLeft: Math.round(pr.left), docRight: Math.round(pr.right), vw: innerWidth, gridScrollW: grid?.scrollWidth, gridClientW: grid?.clientWidth, scrollerW: sc?.clientWidth, ths };
  });
  await p.pdf({ path: `${OUT}/print-users-${lang}-a4.pdf`, format: 'A4' });
  await p.screenshot({ path: `${OUT}/08-print-a4-users-${lang}.jpg`, type: 'jpeg', quality: 70, fullPage: false });
  await ctx.close();
}
save('print3.json', res);
await b.close();
