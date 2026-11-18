import { open, shot } from './lib.mjs';
for (const [email, loc, name] of [['admin@alnoor.example','en-GB','c09-en-history'], ['admin.ar@alnoor.example','ar-AE','c08-ar-history']]) {
  const { browser, page, errors } = await open(email, { locale: loc });
  await page.keyboard.press('g'); await page.keyboard.press('n');
  await page.waitForURL(/numbering/); await page.waitForTimeout(1000);
  await page.keyboard.type('SO- DXB'); await page.waitForTimeout(1200);
  await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
  await page.waitForSelector('input[name=prefix]'); await page.waitForTimeout(600);
  await page.keyboard.press('Alt+y'); await page.waitForTimeout(2500);
  const d = page.locator('[role=dialog]').last();
  console.log(name, (await d.innerText().catch(()=>'(no dialog)')).replace(/\s+/g,' ').slice(0, 900));
  await shot(page, name);
  console.log('errors', errors);
  await browser.close();
}
