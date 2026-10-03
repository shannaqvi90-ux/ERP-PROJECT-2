import { chromium } from 'playwright-core';
const B='http://localhost:20550'; const EV='/home/user/evidence-staging/p05-list-search/r1/';
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const log=(...a)=>console.log(...a);
async function session(email){
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();
  page.errors=[]; page.on('console', m => { if (m.type()==='error') page.errors.push(m.text()); });
  await page.goto(B + '/'); await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.locator('nav.navpane a').first().waitFor();
  return page;
}
const shot=(page,name)=>page.screenshot({ path: EV+name, type:'jpeg', quality:70 });
const focused=(page)=>page.evaluate(()=>{const e=document.activeElement; return e? (e.tagName+' '+(e.getAttribute('aria-label')||e.textContent||'').trim().slice(0,60)):null});
const count=(page)=>page.locator('.list-count').innerText();
const p = await session('admin@alnoor.example');
await p.locator('nav.navpane a[href="/identity/users"]').click();
await p.locator('main table tbody tr').first().waitFor();
// keyboard: Tab from search to header controls
let path=[]; for (let i=0;i<14;i++){ await p.keyboard.press('Tab'); path.push(await focused(p)); }
log('Tab order from search:', path.join(' -> '));
// open E-mail options via keyboard
await p.locator('button[aria-label="Options for the column E-mail"]').focus();
await p.keyboard.press('Enter'); await p.waitForTimeout(300);
log('menu items:', await p.locator('[role=menu] [role=menuitem], [role=menu] button').evaluateAll(b=>b.map(x=>x.textContent.trim())));
log('focused in menu:', await focused(p));
await shot(p,'04-header-menu-keyboard.jpg');
// find "Filter" item and use keyboard
const items = await p.locator('[role=menu] [role=menuitem]').evaluateAll(b=>b.map(x=>x.textContent.trim()));
const fi = items.findIndex(x=>/filter/i.test(x));
for (let i=0;i<fi;i++) await p.keyboard.press('ArrowDown');
await p.keyboard.press('Enter'); await p.waitForTimeout(300);
log('filter dialog focused:', await focused(p));
await p.keyboard.press('Tab'); log('tab ->', await focused(p));
await p.keyboard.type('wang'); await p.keyboard.press('Enter'); await p.waitForTimeout(1200);
log('after filter: count', await count(p), 'chips', await p.locator('.list-chips').innerText().catch(()=>'-'), 'url', p.url());
// group by language via header options
await p.locator('button[aria-label="Options for the column Language"]').click(); await p.waitForTimeout(200);
log('language menu:', await p.locator('[role=menu] [role=menuitem]').evaluateAll(b=>b.map(x=>x.textContent.trim())));
await p.locator('[role=menu] [role=menuitem]', { hasText: /group/i }).first().click(); await p.waitForTimeout(1200);
log('grouped rows:', (await p.locator('main table').innerText()).replace(/\n/g,' | ').slice(0,300));
await shot(p,'05-filter-chip-and-grouping.jpg');
await p.keyboard.press('Escape');
await p.locator('button[aria-label^="Remove the grouping"], .list-chip button').last().click().catch(()=>{});
await p.waitForTimeout(800);
// column chooser
await p.getByRole('button', { name: 'Columns' }).click(); await p.waitForTimeout(300);
log('column chooser:', (await p.locator('.list-columns').innerText()).replace(/\n/g,' | '));
await shot(p,'06-column-chooser.jpg');
await p.locator('.list-columns input[type=checkbox]').nth(5).click().catch(e=>log('chk',e.message));
await p.keyboard.press('Escape'); await p.waitForTimeout(300);
log('headers now:', await p.$$eval('thead th', ths => ths.map(th => th.innerText.split('\n')[0]).join(',')));
// save view
await p.getByRole('button', { name: /^View:/ }).click(); await p.waitForTimeout(300);
log('views menu:', (await p.locator('.list-views').innerText()).replace(/\n/g,' | '));
await p.locator('.list-views [role=menuitem]', { hasText: /save as/i }).first().click(); await p.waitForTimeout(300);
log('save dialog:', (await p.locator('.list-save').innerText()).replace(/\n/g,' | '));
await p.locator('.list-save input').first().fill('Wang shared default');
const checks = p.locator('.list-save input[type=checkbox]'); const n = await checks.count(); for (let i=0;i<n;i++) await checks.nth(i).check();
await p.locator('.list-save button[type=submit]').click(); await p.waitForTimeout(800);
log('after save: view button', await p.getByRole('button', { name: /^View:/ }).innerText(), 'url', p.url());
// reload, default view applied?
await p.goto(B + '/identity/users'); await p.locator('main table tbody tr').first().waitFor(); await p.waitForTimeout(800);
log('reload: view', await p.getByRole('button', { name: /^View:/ }).innerText(), 'count', await count(p));
// viewer sees the shared default view, cannot share
const v = await session('viewer@alnoor.example');
await v.locator('nav.navpane a[href="/identity/users"]').click(); await v.locator('main table tbody tr').first().waitFor(); await v.waitForTimeout(800);
log('viewer: view', await v.getByRole('button', { name: /^View:/ }).innerText(), 'count', await v.locator('.list-count').innerText(), 'new user btn', await v.getByRole('button',{name:'New user'}).count());
await v.getByRole('button', { name: /^View:/ }).click(); await v.locator('.list-views [role=menuitem]', { hasText: /save as/i }).first().click(); await v.waitForTimeout(300);
log('viewer save dialog:', (await v.locator('.list-save').innerText()).replace(/\n/g,' | '));
log('errors', p.errors, v.errors);
await browser.close();
