import { chromium } from 'playwright-core';
const B='http://localhost:20550'; const EV='/home/user/evidence-staging/p05-list-search/r1/';
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } }); const p = await ctx.newPage();
await p.goto(B + '/'); await p.locator('input[name="email"]:focus').waitFor();
await p.keyboard.type('admin@alnoor.example'); await p.keyboard.press('Tab'); await p.keyboard.type('Demo-Pass-2026'); await p.keyboard.press('Enter');
await p.locator('nav.navpane a').first().waitFor();
await p.locator('nav.navpane a[href="/identity/users"]').click(); await p.locator('main table tbody tr').first().waitFor();
const focused=()=>p.evaluate(()=>{const e=document.activeElement; return e? (e.tagName+' '+(e.getAttribute('aria-label')||e.textContent||'').trim().slice(0,50)):null});
for (const key of ['Enter',' ']) {
  await p.goto(B + '/identity/users'); await p.locator('main table tbody tr').first().waitFor(); await p.waitForTimeout(500);
  // keyboard only: Tab from the search box until the E-mail options button
  for (let i=0;i<20;i++){ await p.keyboard.press('Tab'); if ((await focused()).includes('Options for the column E-mail')) break; }
  log(`focused before ${JSON.stringify(key)}:`, await focused());
  await p.keyboard.press(key); await p.waitForTimeout(500);
  log(`  after: url=${p.url()} focused=${await focused()} menu=${await p.locator('[role=menu]').count()}`);
  if (key==='Enter') await p.screenshot({ path: EV+'07-enter-on-column-options-opens-record.jpg', type:'jpeg', quality:70 });
  // sort button too
  await p.goto(B + '/identity/users'); await p.locator('main table tbody tr').first().waitFor(); await p.waitForTimeout(500);
  for (let i=0;i<20;i++){ await p.keyboard.press('Tab'); if ((await focused())==='BUTTON Name') break; }
  await p.keyboard.press(key); await p.waitForTimeout(700);
  log(`  sort button + ${JSON.stringify(key)}: url=${p.url()} focused=${await focused()} first=${(await p.locator('main table tbody tr').first().innerText()).split('\n')[0]}`);
}
function log(...a){console.log(...a)}
await browser.close();
