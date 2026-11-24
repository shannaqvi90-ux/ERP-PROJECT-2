// s1: home, contacts list at 100,000 rows, cut text, resize handles, record pane, palette modes (English, light)
import { browser, signIn, shot, CUT, BASE } from './lib.mjs';
const b = await browser();
const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: 'light' });
const page = await ctx.newPage();
const errors = []; page.on('pageerror', e => errors.push(String(e))); page.on('response', r => { if (r.status() >= 500) errors.push(r.status() + ' ' + r.url()); });
await signIn(page, 'admin@alnoor.example');
const look = await page.evaluate(() => ({ theme: document.documentElement.dataset.theme, density: document.documentElement.dataset.density, dir: document.documentElement.dir, ls: { ...localStorage } }));
console.log('after sign-in', JSON.stringify(look).slice(0, 400));
console.log('home text:', (await page.locator('main').innerText()).slice(0, 900).replace(/\n+/g, ' | '));
console.log('home svgs (sparklines etc.):', await page.locator('main svg').count());
await shot(page, '01-home-en');
// Contacts by keyboard: G then O
await page.keyboard.press('g'); await page.keyboard.press('o');
await page.waitForTimeout(3000);
console.log('url', page.url());
const sk = await page.locator('.skeleton,[class*=skeleton]').count();
console.log('skeletons now', sk);
const info = await page.evaluate(() => ({
  rowsShown: document.querySelectorAll('tbody tr').length,
  headers: [...document.querySelectorAll('thead th')].map(t => t.textContent.trim()),
  resizeHandles: document.querySelectorAll('[role=separator], .col-resize, [class*=resize]').length,
  count: document.querySelector('main')?.innerText.match(/[\d,]+ contacts/)?.[0],
  scrollW: document.documentElement.scrollWidth, w: innerWidth,
}));
console.log(JSON.stringify(info));
const cut = await page.evaluate(CUT);
console.log('cut total', cut.length, 'untitled', cut.filter(c => !c.titled).length);
console.log(JSON.stringify(cut.filter(c => !c.titled).slice(0, 15)));
console.log(JSON.stringify(cut.filter(c => c.titled).slice(0, 5)));
// Hover a cut cell and look for a title or tooltip
const target = await page.evaluate(() => { const td = [...document.querySelectorAll('tbody td')].find(c => c.scrollWidth > c.clientWidth + 1); if (!td) return null; const r = td.getBoundingClientRect(); return { x: r.x + 10, y: r.y + r.height / 2, text: td.textContent }; });
console.log('cut cell', JSON.stringify(target));
if (target) { await page.mouse.move(target.x, target.y); await page.waitForTimeout(1500); console.log('tooltip els', await page.locator('[role=tooltip]').allInnerTexts()); const t = await page.evaluate(({ x, y }) => { const e = document.elementFromPoint(x, y); return e?.title || e?.closest('[title]')?.title; }, target); console.log('title at point', t); }
await shot(page, '02-contacts-en-light');
// Resize a column by its handle if present
const h = page.locator('thead [role=separator]').first();
if (await h.count()) { const bb = await h.boundingBox(); const th = await page.locator('thead th').first().boundingBox(); await page.mouse.move(bb.x + bb.width / 2, bb.y + bb.height / 2); await page.mouse.down(); await page.mouse.move(bb.x + 80, bb.y + bb.height / 2, { steps: 5 }); await page.mouse.up(); const th2 = await page.locator('thead th').first().boundingBox(); console.log('col width', th.width, '->', th2.width); }
// Open a record by keyboard
await page.keyboard.press('Escape');
await page.locator('tbody tr').first().click();
await page.waitForTimeout(400);
await page.keyboard.press('Enter'); await page.waitForTimeout(2000);
console.log('url', page.url());
const pane = await page.evaluate(() => ({ tabs: [...document.querySelectorAll('[role=tab]')].map(t => t.textContent.trim() + (t.tabIndex < 0 ? '(-1)' : '')), cmd: [...document.querySelectorAll('[role=toolbar] button, .command-bar button')].map(b => (b.textContent.trim() || b.getAttribute('aria-label')) + (b.querySelector('svg') ? '*' : '')) }));
console.log(JSON.stringify(pane));
const cut2 = await page.evaluate(CUT);
console.log('pane cut untitled', JSON.stringify(cut2.filter(c => !c.titled).slice(0, 12)));
await shot(page, '03-contact-open-en-light');
// Palette modes
for (const q of ['>', '#', 'Al Noor']) {
  await page.keyboard.press('Control+k'); await page.waitForTimeout(500);
  await page.keyboard.type(q); await page.waitForTimeout(1500);
  console.log('palette', q, '=>', (await page.locator('[role=dialog]').innerText()).slice(0, 400).replace(/\n+/g, ' | '));
  if (q === 'Al Noor') { await page.keyboard.press('Tab'); await page.waitForTimeout(800); console.log('palette Tab =>', (await page.locator('[role=dialog]').innerText()).slice(0, 400).replace(/\n+/g, ' | ')); await shot(page, '04-palette-tab-actions'); }
  await page.keyboard.press('Escape'); await page.waitForTimeout(300);
}
// Sidebar
const nav = await page.evaluate(() => { const n = document.querySelector('nav'); return { svgs: n?.querySelectorAll('svg').length, text: n?.innerText.slice(0, 500).replace(/\n+/g, ' | '), buttons: [...(n?.querySelectorAll('button') ?? [])].map(b => b.getAttribute('aria-label') || b.textContent.trim()).slice(0, 30) }; });
console.log('nav', JSON.stringify(nav));
console.log('errors', JSON.stringify(errors));
await b.close();
