import { createRequire } from 'node:module';
const require = createRequire('/home/shan/critic/p11-attachments-r1/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
import fs from 'node:fs';
const B = 'http://localhost:21150', AC = process.env.AC, OUT = '/home/shan/evidence-staging/p11-attachments/r1';
const log = (...a) => console.log(...a);
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
async function session(login) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, acceptDownloads: true });
  const page = await ctx.newPage();
  await page.goto(B + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(login); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.locator('nav').first().waitFor();
  return { ctx, page };
}
const shot = (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 60 });
const sec = page => page.locator('[data-testid="attachments"]');
{
  const { ctx, page } = await session('admin@alnoor.example');
  await page.goto(`${B}/contacts/contacts/${AC}`);
  await sec(page).waitFor();
  await page.waitForTimeout(800);
  await sec(page).scrollIntoViewIfNeeded();
  log('heading:', await sec(page).getByRole('heading').innerText());
  log('rows before:', await sec(page).locator('tbody tr').count());
  // keyboard: Tab until Add files has focus
  await page.locator('body').click({ position: { x: 5, y: 5 } }).catch(() => {});
  let tabs = 0, found = false;
  for (; tabs < 80; tabs++) {
    await page.keyboard.press('Tab');
    const name = await page.evaluate(() => (document.activeElement?.getAttribute('aria-label') || document.activeElement?.innerText || '').trim().slice(0, 40));
    if (name === 'Add files') { found = true; break; }
  }
  log('tabs to Add files:', tabs + 1, found, await page.evaluate(() => document.activeElement.outerHTML.slice(0, 160)));
  let chooser;
  try { [chooser] = await Promise.all([page.waitForEvent('filechooser', { timeout: 5000 }), page.keyboard.press('Enter')]); log('Enter opened chooser'); }
  catch { [chooser] = await Promise.all([page.waitForEvent('filechooser', { timeout: 5000 }), page.keyboard.press('Space')]); log('Enter did NOT open chooser; Space did'); }
  log('chooser multiple:', chooser.isMultiple());
  await chooser.setFiles(['licence-critic.pdf', 'stamp-critic.png', 'delivery-note-critic.txt']);
  await sec(page).locator('tbody tr', { hasText: 'delivery-note-critic.txt' }).waitFor({ timeout: 20000 });
  await page.waitForTimeout(500);
  log('rows after 3 uploads:', await sec(page).locator('tbody tr').count());
  await sec(page).scrollIntoViewIfNeeded();
  await shot(page, '01-en-three-files-attached');
  // refused zip
  const [ch2] = await Promise.all([page.waitForEvent('filechooser'), sec(page).getByRole('button', { name: 'Add files', exact: true }).click()]);
  await ch2.setFiles(['sample.zip']);
  await page.waitForTimeout(1500);
  log('zip message:', (await sec(page).innerText()).split('\n').filter(l => /zip/i.test(l)).join(' | '));
  await shot(page, '02-en-zip-refused');
  // preview png by keyboard
  await sec(page).getByRole('button', { name: 'Preview stamp-critic.png' }).first().focus();
  await page.keyboard.press('Enter');
  await page.locator('[role="dialog"]').waitFor();
  await page.waitForTimeout(800);
  await shot(page, '03-en-preview-image');
  await page.keyboard.press('Escape');
  await page.locator('[role="dialog"]').waitFor({ state: 'detached' }).catch(() => {});
  // preview pdf
  const pv = sec(page).getByRole('button', { name: 'Preview licence-critic.pdf' }).first();
  log('pdf previewable:', await pv.count());
  if (await pv.count()) { await pv.focus(); await page.keyboard.press('Enter'); await page.locator('[role="dialog"]').waitFor(); await page.waitForTimeout(1500); await shot(page, '04-en-preview-pdf'); await page.keyboard.press('Escape'); }
  // txt preview?
  log('txt previewable:', await sec(page).getByRole('button', { name: 'Preview delivery-note-critic.txt' }).count());
  // download by keyboard
  const dl = sec(page).getByRole('link', { name: 'Download licence-critic.pdf' }).first();
  await dl.focus();
  const [download] = await Promise.all([page.waitForEvent('download'), page.keyboard.press('Enter')]);
  const p = await download.path();
  log('download name:', download.suggestedFilename(), 'same bytes:', fs.readFileSync(p).equals(fs.readFileSync('licence-critic.pdf')));
  // drag and drop
  const buf = fs.readFileSync('stamp-critic.png').toString('base64');
  const dt = await page.evaluateHandle(b64 => { const dt = new DataTransfer(); const bin = Uint8Array.from(atob(b64), c => c.charCodeAt(0)); dt.items.add(new File([bin], 'dropped-stamp.png', { type: 'image/png' })); return dt; }, buf);
  await sec(page).dispatchEvent('dragenter', { dataTransfer: dt });
  await sec(page).dispatchEvent('dragover', { dataTransfer: dt });
  await shot(page, '05-en-drag-over');
  await sec(page).dispatchEvent('drop', { dataTransfer: dt });
  await sec(page).locator('tbody tr', { hasText: 'dropped-stamp.png' }).waitFor({ timeout: 15000 });
  log('drop ok, rows:', await sec(page).locator('tbody tr').count());
  // delete by keyboard
  await sec(page).getByRole('button', { name: 'Delete dropped-stamp.png' }).first().focus();
  await page.keyboard.press('Enter');
  await page.locator('[role="dialog"]').waitFor();
  log('dialog focus:', await page.evaluate(() => document.activeElement?.innerText));
  await shot(page, '06-en-delete-confirm');
  await page.locator('[role="dialog"]').getByRole('button', { name: 'Delete', exact: true }).focus();
  await page.keyboard.press('Enter');
  await sec(page).locator('tbody tr', { hasText: 'dropped-stamp.png' }).waitFor({ state: 'detached', timeout: 10000 });
  log('deleted; rows:', await sec(page).locator('tbody tr').count());
  // where is the section on the page: y offset vs viewport
  await page.evaluate(() => window.scrollTo(0, 0));
  const box = await sec(page).boundingBox();
  log('section top y at scroll 0:', box?.y, 'viewport h 900');
  await shot(page, '07-en-contact-top');
  // history of the contact: look for a History button/tab
  const body = await page.locator('main').innerText();
  log('history words:', /history/i.test(body));
  await ctx.close();
}
{
  const { ctx, page } = await session('admin.ar@alnoor.example');
  await page.goto(`${B}/contacts/contacts/${AC}`);
  await sec(page).waitFor(); await page.waitForTimeout(800);
  await sec(page).scrollIntoViewIfNeeded();
  log('ar dir:', await page.evaluate(() => document.documentElement.dir), 'heading:', await sec(page).getByRole('heading').innerText());
  log('ar text:', (await sec(page).innerText()).slice(0, 400).replace(/\n/g, ' | '));
  await shot(page, '08-ar-attachments-rtl');
  await sec(page).getByRole('button', { name: /معاينة/ }).first().focus().catch(() => {});
  await page.keyboard.press('Enter'); await page.waitForTimeout(1000);
  await shot(page, '09-ar-preview');
  await page.keyboard.press('Escape');
  await ctx.close();
}
{
  const { ctx, page } = await session('viewer@alnoor.example');
  await page.goto(`${B}/contacts/contacts/${AC}`);
  await sec(page).waitFor(); await page.waitForTimeout(800);
  log('viewer add:', await sec(page).getByRole('button', { name: 'Add files' }).count(), 'delete:', await sec(page).getByRole('button', { name: /^Delete / }).count(), 'download links:', await sec(page).getByRole('link', { name: /^Download / }).count());
  await ctx.close();
}
await browser.close();
