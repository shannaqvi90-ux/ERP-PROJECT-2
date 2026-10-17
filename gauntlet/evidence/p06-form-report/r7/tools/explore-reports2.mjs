import { browser, signIn, BASE, OUT, log } from './lib.mjs';
const b = await browser();
for (const [email, tag, name] of [['admin@alnoor.example', 'en', 'Roles and access'], ['admin.ar@alnoor.example', 'ar', 'الأدوار والصلاحيات']]) {
  const { page, ctx } = await signIn(b, email);
  await page.goto(BASE + '/reports/catalog'); await page.waitForTimeout(1200);
  const btn = page.locator('main button', { hasText: name }).first();
  await btn.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(800);
  log(tag, 'focused after Enter', await page.evaluate(() => document.activeElement?.outerHTML.slice(0, 150)));
  await page.keyboard.press('Control+Enter'); await page.waitForTimeout(1500);
  const txt = await page.locator('main').textContent();
  log(tag, 'result', JSON.stringify(txt.slice(txt.indexOf(name, 300), txt.indexOf(name, 300) + 900)));
  log(tag, 'buttons', JSON.stringify(await page.locator('main button').allTextContents()));
  await page.screenshot({ path: `${OUT}/report-role-summary-${tag}.png` });
  // Grouping change: look for a group-by select
  const sel = page.locator('main select');
  log(tag, 'selects', await sel.count(), JSON.stringify(await sel.evaluateAll(s => s.map(x => x.name + ':' + [...x.options].map(o => o.value).join('/')))));
  await ctx.close();
}
await b.close();
