import { browser, signIn, BASE, OUT, log } from './lib.mjs';
const b = await browser();
for (const [email, tag] of [['admin@alnoor.example', 'en'], ['admin.ar@alnoor.example', 'ar']]) {
  const { page, ctx } = await signIn(b, email);
  await page.goto(BASE + '/reports/catalog'); await page.waitForTimeout(1500);
  log(tag, 'reports page text', JSON.stringify((await page.locator('main').textContent()).slice(0, 400)));
  // keyboard: Tab to first report and open with Enter
  const links = page.locator('main a, main button');
  log(tag, 'controls', await links.count());
  // open role summary
  const rs = page.getByText(tag === 'en' ? 'Roles and permissions' : 'الأدوار والصلاحيات').first();
  if (await rs.count()) { await rs.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(800); }
  await page.keyboard.press('Control+Enter'); await page.waitForTimeout(1500);
  log(tag, 'after run', JSON.stringify((await page.locator('main').textContent()).slice(0, 700)));
  await page.screenshot({ path: `${OUT}/report-role-summary-${tag}.png` });
  // print/pdf/export buttons
  const btns = await page.locator('main button').allTextContents();
  log(tag, 'buttons', JSON.stringify(btns));
  // try a download of xlsx
  const xl = page.locator('main button, main a').filter({ hasText: /XLSX|Excel|إكسل/i }).first();
  if (await xl.count()) {
    const [dl] = await Promise.all([page.waitForEvent('download', { timeout: 15000 }).catch(() => null), xl.click()]);
    log(tag, 'xlsx download', dl ? dl.suggestedFilename() : 'none');
    if (dl) await dl.saveAs(`${OUT}/ui-role-summary-${tag}.xlsx`);
  }
  await ctx.close();
}
await b.close();
