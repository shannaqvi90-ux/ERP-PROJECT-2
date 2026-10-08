import fs from 'node:fs';
import { open, B, OUT, shot } from './common.mjs';
const log = []; const L = (...a) => { const s = a.join(' '); log.push(s); console.log(s); };
const CID = '018dca08-f08c-7511-8908-1621e033e7ef'; // ALN-AUH
const tok = fs.readFileSync(`${OUT}/tok-admin-alnoor.example.txt`, 'utf8').trim();
const H = { authorization: 'Bearer ' + tok, 'X-Erp-Request': '1', 'content-type': 'application/json' };
{
  const { browser, page, writes } = await open('admin@alnoor.example');
  await page.goto(B + '/tenancy/companies/' + CID);
  const phone = page.locator('[data-field="phone"] input'); await phone.waitFor();
  const orig = await phone.inputValue();
  // someone else changes the record meanwhile
  const rec = await (await fetch(`${B}/api/tenancy/companies/${CID}`, { headers: H })).json();
  const r = await fetch(`${B}/api/tenancy/companies/${CID}`, { method: 'PUT', headers: H, body: JSON.stringify({ ...rec, website: 'https://other.example.ae' }) });
  L('other user PUT', r.status);
  await phone.click(); await page.keyboard.type('+971 2 000 3333');
  const resp = page.waitForResponse(x => x.request().method() === 'PUT'); await page.keyboard.press('Control+s'); const rr = await resp;
  L('stale save status', rr.status(), (await rr.text()).slice(0, 300)); await page.waitForTimeout(800);
  L('form after conflict', (await page.locator('.record-form').innerText()).split('\n').slice(0, 8).join(' | '));
  await shot(page, '08-form-en-edit-conflict');
  const reload = page.getByRole('button', { name: /latest/i });
  L('reload-latest button', await reload.count());
  if (await reload.count()) { await reload.click(); await page.waitForTimeout(800); L('after reload: website', await page.locator('[data-field="website"] input').inputValue(), 'phone', await phone.inputValue()); }
  // restore website
  const rec2 = await (await fetch(`${B}/api/tenancy/companies/${CID}`, { headers: H })).json();
  await fetch(`${B}/api/tenancy/companies/${CID}`, { method: 'PUT', headers: H, body: JSON.stringify({ ...rec2, website: rec.website, phone: orig }) });
  await browser.close();
}
{
  const { browser, page } = await open('admin.ar@alnoor.example');
  await page.goto(B + '/identity/roles'); await page.waitForTimeout(1500);
  await page.keyboard.press('Alt+Shift+r'); await page.waitForTimeout(500);
  L('ar list print menu via Alt+Shift+R', JSON.stringify(await page.getByRole('menuitem').allInnerTexts()));
  L('hrefs', JSON.stringify(await page.getByRole('menuitem').evaluateAll(es => es.map(e => e.getAttribute('href')))));
  L('focused', await page.evaluate(() => document.activeElement?.textContent));
  await shot(page, '09-list-print-menu-ar');
  await browser.close();
}
fs.writeFileSync(`${OUT}/browser/explore-conflict-printmenu.log`, log.join('\n'));
