import { api, browser, signIn, BASE, log } from './lib.mjs';
const a = await api('admin@alnoor.example');
const co = (await a.call('GET', '/api/tenancy/companies?take=50')).body.items.find(c => c.code === 'ALN-DXB');
const rec0 = (await a.call('GET', '/api/tenancy/companies/' + co.id)).body;
const b = await browser();
for (const vp of [{ width: 1400, height: 900 }, { width: 1920, height: 1080 }]) {
  const { page, ctx } = await signIn(b, 'admin@alnoor.example', { viewport: vp });
  await page.goto(`${BASE}/tenancy/companies/${co.id}`); const ph = page.locator('[data-field="phone"] input'); await ph.waitFor(); await page.waitForTimeout(600);
  await ph.click(); await page.keyboard.type('505550199'); await page.waitForTimeout(1500);
  const n = page.locator('.record-form .notice');
  const inView = await n.evaluate(el => { const r = el.getBoundingClientRect(); let p = el.parentElement; while (p && !(p.scrollHeight > p.clientHeight && /auto|scroll/.test(getComputedStyle(p).overflowY))) p = p.parentElement; const pr = p ? p.getBoundingClientRect() : { top: 0, bottom: innerHeight }; return { top: r.top, bottom: r.bottom, scrollerTop: pr.top, scrollerBottom: pr.bottom, visible: r.bottom > pr.top && r.top < pr.bottom && r.top >= 0 }; });
  const live = await n.getAttribute('role');
  log(JSON.stringify(vp), 'notice text', JSON.stringify(await n.textContent()), 'role', live, 'geometry', JSON.stringify(inView));
  await page.keyboard.press('Alt+z'); await page.waitForTimeout(1200);
  await ctx.close();
}
const now = (await a.call('GET', '/api/tenancy/companies/' + co.id)).body; log('phone back', now.phone === rec0.phone);
await b.close();
