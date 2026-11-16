import { chromium } from '/home/shan/critic/p11-attachments-r3-plant/gauntlet/compare/node_modules/playwright-core/index.mjs';
import fs from 'fs';
const BASE = 'http://localhost:21150';
const OUT = '/home/shan/evidence-staging/p11-attachments/r3';
const lang = process.argv[2] || 'en';
const email = process.argv[3] || (lang === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example');
const tag = process.argv[4] || lang;
const notes = [];
const note = (...a) => { const l = a.join(' '); notes.push(l); console.log(l); };
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, acceptDownloads: true });
const page = await ctx.newPage();
const errors = [];
page.on('response', r => { if (r.status() >= 500) errors.push(`${r.status()} ${r.url()}`); });
page.on('pageerror', e => errors.push('pageerror ' + e.message));
const shot = async (n) => page.screenshot({ path: `${OUT}/${n}.jpg`, type: 'jpeg', quality: 70 });
await page.goto(BASE + '/');
await page.evaluate((l) => { localStorage.clear(); localStorage.setItem('erp.language', l); }, lang);
await page.goto(BASE + '/');
await page.locator('input[name="email"]').waitFor();
await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.waitForTimeout(2500);
note('dir', await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang));
await page.goto(BASE + '/contacts/contacts');
await page.locator('.list-search input').waitFor();
await page.locator('.list-search input').focus();
await page.keyboard.type(process.env.CODE || 'C036634');
await page.waitForTimeout(1500);
await page.keyboard.press('Enter');
const section = page.locator('[data-testid="attachments"]');
await section.waitFor({ timeout: 20000 }).catch(e => note('NO SECTION', e.message));
await page.waitForTimeout(1500);
note('url', page.url());
const clip = page.locator('[data-record-panel="attachments.files"]');
note('paperclip', await clip.count(), await clip.getAttribute('aria-label').catch(() => ''), await clip.getAttribute('title').catch(() => ''));
note('names line', await page.locator('[data-testid="attachments-names"]').innerText().catch(() => 'none'));
if (process.argv[5] === 'viewer') {
  await shot(`${tag}-viewer-contact-top`);
  await section.scrollIntoViewIfNeeded();
  note('viewer add buttons', await section.getByRole('button', { name: /Add files|إضافة/ }).count(), 'delete buttons', await section.locator('button', { hasText: /^(Delete|حذف)$/ }).count(), 'download links', await section.locator('a[download]').count());
  await shot(`${tag}-viewer-section`);
  note('errors', JSON.stringify(errors)); fs.writeFileSync(`${OUT}/browser/notes-${tag}.txt`, notes.join('\n')); await browser.close(); process.exit(0);
}
await shot(`${tag}-01-contact-top`);
// Alt+A, Enter opens first file
await page.keyboard.press('Alt+KeyA');
await page.waitForTimeout(800);
note('focused after Alt+A', await page.evaluate(() => (document.activeElement?.getAttribute('aria-label') || document.activeElement?.textContent || '').slice(0, 80)));
const focusedName = await page.evaluate(() => document.activeElement?.textContent || '');
if (!/Add files|إضافة/.test(focusedName)) await page.keyboard.press('Enter');
await page.waitForTimeout(1500);
note('dialog after Enter', await page.locator('[role="dialog"]').count(), (await page.locator('[role="dialog"]').innerText().catch(() => '')).slice(0, 100).replace(/\n/g, ' | '));
await shot(`${tag}-02-altA-enter-viewer`);
await page.keyboard.press('Escape');
await page.waitForTimeout(500);
// Keyboard upload: focus Add files then Enter -> file chooser
const stem = `crit-${lang}-${Date.now() % 100000}`;
const add = section.locator('button').filter({ hasText: /Add files|إضافة ملفات|إضافة/ }).first();
note('add button text', await add.innerText().catch(() => 'none'));
await section.locator('h3').first().click();
await add.focus();
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64');
const pdf = Buffer.from('%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n');
const txt = Buffer.from(lang === 'ar' ? 'مذكرة تسليم رقم ٤٥\nسطر ثاني' : 'Delivery memo 45\nsecond line');
let chooserOk = false;
try {
  const [fc] = await Promise.all([page.waitForEvent('filechooser', { timeout: 8000 }), page.keyboard.press('Enter')]);
  note('file chooser multiple', fc.isMultiple());
  await fc.setFiles([{ name: `${stem}-seal.png`, mimeType: 'image/png', buffer: png }, { name: `${stem}-licence.pdf`, mimeType: 'application/pdf', buffer: pdf }, { name: `${stem}-memo.txt`, mimeType: 'text/plain', buffer: txt }]);
  chooserOk = true;
} catch (e) { note('FILECHOOSER FAIL', e.message); }
await page.waitForTimeout(3000);
const rows = section.locator('table tbody tr');
note('rows with stem', await rows.filter({ hasText: stem }).count(), 'chooser', chooserOk);
await section.scrollIntoViewIfNeeded();
await shot(`${tag}-03-three-uploaded-keyboard`);
// Drag and drop
await page.evaluate(async (stem) => {
  const el = document.querySelector('[data-testid="attachments"]');
  const dt = new DataTransfer();
  dt.items.add(new File(['dropped text ' + stem], stem + '-dropped.txt', { type: 'text/plain' }));
  dt.items.add(new File(['PK\u0003\u0004'], stem + '-bad.exe', { type: 'application/octet-stream' }));
  el.dispatchEvent(new DragEvent('dragenter', { dataTransfer: dt, bubbles: true }));
  el.dispatchEvent(new DragEvent('dragover', { dataTransfer: dt, bubbles: true }));
}, stem);
await page.waitForTimeout(300);
await shot(`${tag}-04-drag-over`);
await page.evaluate(async (stem) => {
  const el = document.querySelector('[data-testid="attachments"]');
  const dt = new DataTransfer();
  dt.items.add(new File(['dropped text ' + stem], stem + '-dropped.txt', { type: 'text/plain' }));
  dt.items.add(new File(['MZ binary'], stem + '-bad.exe', { type: 'application/octet-stream' }));
  el.dispatchEvent(new DragEvent('drop', { dataTransfer: dt, bubbles: true }));
}, stem);
await page.waitForTimeout(3000);
note('dropped row', await rows.filter({ hasText: `${stem}-dropped.txt` }).count(), 'exe row', await rows.filter({ hasText: `${stem}-bad.exe` }).count());
note('section messages', (await section.locator('[role="alert"], .error, [role="status"]').allInnerTexts()).join(' / ').slice(0, 300));
await shot(`${tag}-05-drop-result-refusal`);
// Preview txt by keyboard: focus Preview button of memo, Enter
const prevMemo = section.getByRole('button', { name: new RegExp(`${stem}-memo.txt`) }).first();
note('memo buttons', await section.getByRole('button', { name: new RegExp(`${stem}-memo.txt`) }).count());
await prevMemo.focus().catch(() => {}); await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
note('text viewer', (await page.locator('[role="dialog"]').innerText().catch(() => 'none')).slice(0, 120).replace(/\n/g, ' | '));
await shot(`${tag}-06-text-preview`);
await page.keyboard.press('Escape'); await page.waitForTimeout(1000);
// PDF preview
const prevPdf = section.getByRole('button', { name: new RegExp(`${stem}-licence.pdf`) }).first();
await prevPdf.focus().catch(() => {}); await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
note('pdf iframe', await page.locator('[role="dialog"] iframe').getAttribute('src').catch(() => 'none'));
await page.keyboard.press('Escape'); await page.waitForTimeout(1000);
// Download by keyboard
const dl = section.locator(`a[download="${stem}-licence.pdf"]`).first();
await dl.focus().catch(() => {});
try { const [d] = await Promise.all([page.waitForEvent('download', { timeout: 8000 }), page.keyboard.press('Enter')]); const p = await d.path(); note('download', d.suggestedFilename(), fs.readFileSync(p).equals(pdf)); } catch (e) { note('DOWNLOAD FAIL', e.message); }
// Ctrl+K search by file name
await page.keyboard.press('Control+KeyK'); await page.waitForTimeout(600);
await page.keyboard.type(`${stem}-sea`); await page.waitForTimeout(2000);
note('palette', (await page.locator('[role="dialog"], [role="listbox"]').first().innerText().catch(() => 'none')).slice(0, 300).replace(/\n/g, ' | '));
await shot(`${tag}-07-palette-file-search`);
await page.keyboard.press('Enter'); await page.waitForTimeout(2500);
note('after palette Enter', page.url(), 'dialog', (await page.locator('[role="dialog"]').innerText().catch(() => 'none')).slice(0, 80).replace(/\n/g, ' | '), 'img', await page.locator('[role="dialog"] img').count());
await page.keyboard.press('Escape'); await page.waitForTimeout(1000);
// Delete by keyboard
const del = section.locator('button').filter({ hasText: /^(Delete|حذف)$/ });
const delSeal = section.getByRole('button', { name: new RegExp(`(Delete|حذف).*${stem}-seal.png`) }).first();
note('delete seal found', await section.getByRole('button', { name: new RegExp(`(Delete|حذف).*${stem}-seal.png`) }).count());
await delSeal.focus().catch(() => {}); await page.keyboard.press('Enter'); await page.waitForTimeout(800);
note('delete dialog', (await page.locator('[role="dialog"]').innerText().catch(() => 'none')).slice(0, 200).replace(/\n/g, ' | '));
await shot(`${tag}-08-delete-confirm`);
note('focused in dialog', await page.evaluate(() => document.activeElement?.textContent?.slice(0, 40)));
await page.keyboard.press('Tab');
note('focused after Tab', await page.evaluate(() => document.activeElement?.textContent?.slice(0, 40)));
await page.keyboard.press('Enter'); await page.waitForTimeout(2000);
note('seal rows after delete', await rows.filter({ hasText: `${stem}-seal.png` }).count());
// History panel
await page.keyboard.press('Alt+KeyY'); await page.waitForTimeout(2000);
note('history', (await page.locator('[data-record-panel], aside, [role="complementary"]').filter({ hasText: /file|ملف|seal|crit/ }).first().innerText().catch(() => 'none')).slice(0, 400).replace(/\n/g, ' | '));
await shot(`${tag}-09-history`);
// English leftover check in Arabic
if (lang === 'ar') {
  const txtAll = await section.innerText();
  note('latin words in AR section', JSON.stringify((txtAll.match(/\b[A-Za-z]{3,}\b/g) || []).filter(w => !w.startsWith('crit') && !/^(png|pdf|txt|exe|seal|licence|memo|dropped|bad|Trade|Delivery|note|licence|company|KB|MB)$/i.test(w)).slice(0, 30)));
}
// clean up files of this run
const id = /contacts\/contacts\/([0-9a-f-]{36})/.exec(page.url())?.[1];
const list = await page.evaluate(async (id) => (await (await fetch(`/api/attachments/contacts.contacts/${id}/files`)).json()), id);
for (const f of list.items.filter(f => f.name.startsWith('crit-'))) await page.evaluate(async (fid) => fetch(`/api/attachments/contacts.contacts/files/${fid}`, { method: 'DELETE', headers: { 'X-Erp-Request': '1' } }), f.id);
note('errors', JSON.stringify(errors));
fs.writeFileSync(`${OUT}/browser/notes-${tag}.txt`, notes.join('\n'));
await browser.close();
