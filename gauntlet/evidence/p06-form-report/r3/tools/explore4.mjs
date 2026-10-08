import fs from 'node:fs';
import { open, B, OUT, shot } from './common.mjs';
const log = []; const L = (...a) => { const s = a.join(' '); log.push(s); console.log(s); };
const CID = '018dca08-f085-7542-af56-a574b8de3616';
{
  const { browser, page, writes } = await open('admin.ar@alnoor.example');
  await page.goto(B + '/tenancy/companies/' + CID);
  const f = n => page.locator(`[data-field="${n}"] input`);
  await f('phone').waitFor();
  L('html dir/lang', await page.evaluate(() => document.documentElement.dir + ' ' + document.documentElement.lang));
  L('header', (await page.locator('.record-header').innerText()).replace(/\n/g, ' | '));
  L('sections', JSON.stringify(await page.locator('.record-form legend, [role=tab]').allInnerTexts()));
  const box = async n => { const b = await page.locator(`[data-field="${n}"]`).boundingBox(); return Math.round(b.x); };
  L('x legalNameEn vs legalNameAr (RTL: En should be right of Ar)', await box('legalNameEn'), await box('legalNameAr'));
  L('phone input dir', await f('phone').evaluate(e => getComputedStyle(e).direction + ' ' + e.dir));
  await f('website').click(); await page.keyboard.press('Control+a'); await page.keyboard.type('xx');
  const resp = page.waitForResponse(r => r.request().method() === 'PUT');
  await page.keyboard.press('Control+s'); await resp; await page.waitForTimeout(600);
  L('ar errors', JSON.stringify(await page.locator('.record-form [role=alert]').allInnerTexts()));
  await page.locator('.record-form').evaluate(e => e.scrollTop = 0);
  await page.locator('.record-header').scrollIntoViewIfNeeded();
  await shot(page, '03-form-ar-rtl-server-error');
  await page.keyboard.press('Escape'); await page.waitForTimeout(400);
  L('ar leave dialog', (await page.locator('[role=dialog], [role=alertdialog]').innerText().catch(() => 'none')).replace(/\n/g, ' | '));
  await shot(page, '04-form-ar-leave-dialog');
  await page.getByRole('button', { name: 'تجاهل التغييرات' }).click().catch(async () => { await page.keyboard.press('Escape'); await page.keyboard.press('Alt+z'); });
  await page.waitForTimeout(500);
  L('after discard url', page.url());
  // reports page in arabic
  await page.goto(B + '/reports/catalog'); await page.waitForTimeout(1500);
  L('reports page ar', (await page.locator('main').innerText()).replace(/\n/g, ' | ').slice(0, 1200));
  await page.screenshot({ path: `${OUT}/browser/reports-ar-initial.png` });
  L('writes', JSON.stringify(writes));
  await browser.close();
}
{
  const { browser, page, writes } = await open('viewer@alnoor.example');
  await page.goto(B + '/tenancy/companies');
  await page.waitForTimeout(1500);
  const rows = await page.locator('[role=grid] [role=row]').count();
  L('viewer companies rows', rows);
  await page.goto(B + '/tenancy/companies/' + '018dca08-f081-79ec-8741-97eafd0562a4');
  await page.waitForTimeout(1500);
  L('viewer form', (await page.locator('main').innerText()).replace(/\n/g, ' | ').slice(0, 600));
  L('disabled inputs', await page.locator('.record-form input:disabled, .record-form select:disabled, .record-form textarea:disabled, .record-form input[readonly]').count(), 'of', await page.locator('.record-form input, .record-form select, .record-form textarea').count());
  const before = writes.length;
  await page.locator('[data-field="phone"] input').click({ force: true }).catch(() => {});
  for (const k of ['Control+s', 'Control+Enter', 'Alt+z', 'Delete', 'Alt+n', 'Alt+PageDown']) { await page.keyboard.press(k); await page.waitForTimeout(250); }
  L('viewer writes after keys', JSON.stringify(writes.slice(before)));
  await shot(page, '05-form-viewer-read-only');
  await browser.close();
}
fs.writeFileSync(`${OUT}/browser/explore-form-ar-viewer.log`, log.join('\n'));
