import { chromium } from 'playwright-core';
const B='http://localhost:20550'; const EV='/home/user/evidence-staging/p05-list-search/r1/';
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const log=(...a)=>console.log(...a);
async function session(email){
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale:'en-GB' });
  const page = await ctx.newPage();
  page.errors=[]; page.on('console', m => { if (m.type()==='error') page.errors.push(m.text()); });
  page.on('response', r => { if (r.status()>=500) page.errors.push('HTTP '+r.status()+' '+r.url()); });
  await page.goto(B + '/'); await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.locator('nav.navpane a').first().waitFor();
  return page;
}
const shot=(page,name)=>page.screenshot({ path: EV+name, type:'jpeg', quality:70 });
const focused=(page)=>page.evaluate(()=>{const e=document.activeElement; return e? (e.tagName+'.'+e.className+' '+(e.getAttribute('aria-label')||e.textContent||'').trim().slice(0,60)):null});
const count=(page)=>page.locator('.list-count').innerText();
const p = await session('admin@alnoor.example');
await p.locator('nav.navpane a[href="/identity/users"]').click();
await p.locator('main table tbody tr').first().waitFor();
log('rows in DOM for 100,004:', await p.locator('main table tbody tr').count());
await shot(p,'01-users-list-en.jpg');
// alignment check: x of email cell across rendered rows
const xs = await p.$$eval('main table tbody tr', trs => trs.slice(0,15).map(tr => Math.round(tr.children[2]?.getBoundingClientRect().x)));
log('email column x per row:', xs.join(','));
// search as you type
const t0=Date.now();
await p.keyboard.type('yousef wang', {delay: 60});
await p.waitForFunction(()=>/46/.test(document.querySelector('.list-count')?.textContent||''), null, {timeout:5000}).then(()=>log('as-you-type updated count without Enter in', Date.now()-t0,'ms:'), ()=>log('as-you-type: count did NOT update without Enter'));
log(' count now', await count(p));
await p.keyboard.press('Control+a'); await p.keyboard.type('Yousef Samir Wang'); await p.keyboard.press('Enter');
await p.waitForTimeout(800);
log('after Enter on single result: url', p.url(), 'focused', await focused(p));
log('record panel text:', (await p.locator('main').innerText()).split('\n').filter(l=>/060019|Yousef/.test(l)).slice(0,4));
await shot(p,'02-search-enter-opens-single-result.jpg');
await p.keyboard.press('Escape'); await p.waitForTimeout(300);
log('after Esc focused', await focused(p), 'url', p.url());
await p.keyboard.press('Escape'); await p.waitForTimeout(300);
log('after 2nd Esc', await focused(p), await count(p));
// grid keyboard nav
await p.locator('input.search').press('ArrowDown'); await p.waitForTimeout(200);
log('ArrowDown from search -> focused', await focused(p));
for (let i=0;i<3;i++) await p.keyboard.press('PageDown');
await p.keyboard.press('End'); await p.waitForTimeout(1500);
log('after End: active row', await p.evaluate(()=>document.querySelector('.list-row.is-active')?.innerText.replace(/\n/g,' | ')), 'aria-rowcount', await p.locator('table[role=grid]').getAttribute('aria-rowcount'));
await p.keyboard.press('Home'); await p.waitForTimeout(500);
await p.keyboard.press('ArrowDown'); await p.keyboard.press('Space'); await p.keyboard.press('Shift+ArrowDown'); await p.keyboard.press('Shift+ArrowDown');
await p.waitForTimeout(200);
log('selection bar:', await p.locator('.list-selectionbar').innerText().catch(()=>'none'));
await p.keyboard.press('Control+a'); await p.waitForTimeout(200);
log('after Ctrl+A:', await p.locator('.list-selectionbar').innerText().catch(()=>'none'));
await shot(p,'03-selection-bulk-bar.jpg');
await p.keyboard.press('Escape');
await p.keyboard.press('Enter'); await p.waitForTimeout(600);
log('Enter on active row opens:', p.url());
await p.keyboard.press('Escape'); await p.waitForTimeout(300);
// header sort/filter by keyboard: find header buttons
const headers = await p.$$eval('thead th', ths => ths.map(th => ({ text: th.innerText.trim(), btns: [...th.querySelectorAll('button')].map(b=>b.getAttribute('aria-label')||b.textContent.trim()) })));
log('headers:', JSON.stringify(headers));
log('errors', p.errors);
await browser.close();
