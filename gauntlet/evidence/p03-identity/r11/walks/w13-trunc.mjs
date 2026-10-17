import { open, signIn, EV, BASE } from './h.mjs';
import fs from 'node:fs';
const log = []; const L = (s) => { log.push(s); console.log(s); };
for (const width of [1366, 1600, 1920]) {
  const { browser, page } = await open({ width, height: 900 });
  await signIn(page, 'admin@alnoor.example');
  await page.goto(BASE + '/identity/users'); await page.waitForTimeout(2500);
  const measure = async () => page.evaluate(() => {
    const grid = document.querySelector('main [role=grid], main table'); const gr = grid.getBoundingClientRect();
    const hdr = [...document.querySelectorAll('main [role=columnheader]')].map((h) => { const r = h.getBoundingClientRect(); const lab = h.querySelector('span,button') ?? h; return `${h.innerText.split('\n')[0] || '·'}${lab.scrollWidth > lab.clientWidth + 1 ? '(cut)' : ''}${r.right > gr.right + 1 || r.left < gr.left - 1 ? '(off-screen)' : ''}`; });
    const cells = [...document.querySelectorAll('main [role=row]')].slice(1, 6).flatMap((row) => [...row.querySelectorAll('[role=gridcell],td')].filter((c) => c.scrollWidth > c.clientWidth + 1).map((c) => c.innerText.slice(0, 20)));
    return { hdr, cutCells: cells.length };
  });
  L(`${width}px list only: ${JSON.stringify(await measure())}`);
  await page.locator('main [role=row]').nth(1).click(); await page.waitForTimeout(1200);
  L(`${width}px with a record open: ${JSON.stringify(await measure())}`);
  await browser.close();
}
fs.writeFileSync(`${EV}/walks/w13-users-list-truncation.txt`, log.join('\n'));
