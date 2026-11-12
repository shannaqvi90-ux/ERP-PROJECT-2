import { open, shot } from './lib.mjs';
const { b, p } = await open('admin.ar');
await p.goto('http://localhost:21050/numbering/sequences'); await p.waitForTimeout(2500);
const cells = await p.locator('[role=row]').nth(1).locator('[role=gridcell], td').evaluateAll(es => es.map(e => [e.innerText, getComputedStyle(e).direction, e.getAttribute('dir'), e.querySelector('[dir]')?.getAttribute('dir')]));
console.log(JSON.stringify(cells));
const row = p.locator('[role=row]').nth(1);
await row.screenshot({ path: '/home/shan/evidence-staging/p10-numbering/r4/walk/ar-row.png' });
// rendered order check: measure x of the first and last characters of the prefix cell
const pos = await p.evaluate(() => {
  const cell = [...document.querySelectorAll('[role=row]')][1].querySelectorAll('[role=gridcell], td');
  const out = [];
  for (const c of cell) { const t = c.innerText.trim(); if (!t.includes('{')) continue;
    const w = document.createTreeWalker(c, NodeFilter.SHOW_TEXT); let n; while ((n = w.nextNode())) { const s = n.textContent; if (!s.trim()) continue;
      const r = document.createRange(); r.setStart(n, 0); r.setEnd(n, 1); const first = r.getBoundingClientRect().x; r.setStart(n, s.length - 1); r.setEnd(n, s.length); const last = r.getBoundingClientRect().x; out.push({ s, firstCharX: first, lastCharX: last }); } }
  return out; });
console.log(JSON.stringify(pos));
await b.close();
