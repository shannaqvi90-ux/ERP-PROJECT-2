import { launch, signIn, save, OUT } from './pw.mjs';
const b = await launch();
const res = {};
const ctx = await b.newContext({ locale: 'ar-AE', viewport: { width: 1440, height: 900 } });
const p = await ctx.newPage();
await signIn(p, 'admin.ar@alnoor.example');
await p.goto(p.url().replace(/\/[^/]*$/, '') + '/reports/catalog');
await p.waitForTimeout(1500);
res.reports = await p.evaluate(() => [...document.querySelectorAll('main a')].map(a => a.textContent.trim() + ' -> ' + a.getAttribute('href')).slice(0, 20));
const links = await p.locator('main nav.reports-catalog a, main .reports-nav a, main nav a[href*=report]').all();
res.perReport = [];
for (let i = 0; i < Math.min(links.length, 6); i++) {
  const a = p.locator('main nav.reports-catalog a, main .reports-nav a, main nav a[href*=report]').nth(i);
  const name = (await a.textContent()).trim();
  await a.click();
  await p.waitForTimeout(800);
  await p.keyboard.press('Control+Enter');
  await p.waitForTimeout(2500);
  await p.setViewportSize({ width: 718, height: 1000 });
  await p.emulateMedia({ media: 'print' });
  await p.waitForTimeout(300);
  const m = await p.evaluate(() => {
    const tables = [...document.querySelectorAll('main table')].filter(t => getComputedStyle(t).display !== 'none' && t.offsetParent !== null);
    return tables.map(t => { const r = t.getBoundingClientRect(); return { left: Math.round(r.left), right: Math.round(r.right), w: Math.round(r.width), cols: t.querySelectorAll('thead th').length }; });
  });
  res.perReport.push({ name, vw: 718, tables: m });
  if (i === 0) await p.pdf({ path: `${OUT}/print-report-ar-a4.pdf`, format: 'A4' });
  await p.emulateMedia({ media: 'screen' });
  await p.setViewportSize({ width: 1440, height: 900 });
}
save('print4.json', res);
await b.close();
