// s2: is every cut cell readable (title on hover or focus)? Name and e-mail columns, scrolled right, Status column reach
import { browser, signIn, shot, BASE } from './lib.mjs';
const b = await browser();
const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: 'light' });
const page = await ctx.newPage();
await signIn(page, process.env.WHO || 'admin@alnoor.example');
await page.goto(BASE + '/contacts/contacts'); await page.waitForTimeout(3000);
const cells = await page.evaluate(() => [...document.querySelectorAll('tbody tr')].slice(0, 25).flatMap((tr, ri) => [...tr.querySelectorAll('td')].map((td, ci) => {
  const leaf = [...td.querySelectorAll('*')].filter(e => e.children.length === 0).find(e => e.scrollWidth > e.clientWidth + 1) || (td.scrollWidth > td.clientWidth + 1 ? td : null);
  if (!leaf) return null; const r = leaf.getBoundingClientRect();
  return { ri, ci, text: leaf.textContent.trim(), x: r.x + Math.min(20, r.width / 2), y: r.y + r.height / 2, visible: r.right <= innerWidth && r.left >= 0 };
})).filter(Boolean));
console.log('cut cells in first 25 rows:', cells.length, 'visible', cells.filter(c => c.visible).length);
let untitled = [];
for (const c of cells.filter(c => c.visible)) {
  await page.mouse.move(c.x, c.y); await page.waitForTimeout(120);
  const t = await page.evaluate(({ x, y }) => { const e = document.elementFromPoint(x, y); const tip = document.querySelector('[role=tooltip]'); return (e?.title || e?.closest('[title]')?.title || '') + (tip ? ' |tip:' + tip.textContent : ''); }, c);
  if (!t || !t.includes(c.text.replace('…', '').slice(0, 10))) untitled.push({ ...c, t });
}
console.log('cut visible cells with no title after hover:', untitled.length, JSON.stringify(untitled.slice(0, 8)));
// Keyboard focus on a cut cell: arrow down then see title of the focused row's cut cell
const tbl = await page.evaluate(() => { const s = document.querySelector('table')?.closest('[class*=scroll], .list-body, .table-wrap') || document.querySelector('table')?.parentElement; return { sw: s?.scrollWidth, cw: s?.clientWidth, cls: s?.className, overflowX: s && getComputedStyle(s).overflowX }; });
console.log('table container', JSON.stringify(tbl));
const status = await page.evaluate(() => { const th = [...document.querySelectorAll('thead th')].find(t => /Status/.test(t.textContent)); const r = th?.getBoundingClientRect(); return r && { left: r.left, right: r.right, vw: innerWidth }; });
console.log('Status header box', JSON.stringify(status));
// Comfortable density: count rows shown and cut cells
await page.getByText('Comfortable', { exact: true }).click(); await page.waitForTimeout(1000);
console.log('comfortable rows', await page.locator('tbody tr').count(), await page.evaluate(() => document.documentElement.dataset.density));
await page.getByText('Compact', { exact: true }).click(); await page.waitForTimeout(500);
await b.close();
