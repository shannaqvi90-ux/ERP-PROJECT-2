// Critic p10 r1 browser walk: English and Arabic, keyboard only where possible.
import { chromium } from '/home/shan/critic/p10-numbering-r1/gauntlet/compare/node_modules/playwright-core/index.mjs';
import fs from 'node:fs';
const base = process.env.BASE || 'http://localhost:21050';
const ev = '/home/shan/evidence-staging/p10-numbering/r1';
const exe = fs.readdirSync(process.env.HOME + '/.cache/ms-playwright').includes('chromium-1243') ? process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' : undefined;
const browser = await chromium.launch({ executablePath: exe });
const notes = [];
const note = (s) => { notes.push(s); console.log(s); };
async function signIn(login) {
  const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 } });
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(String(e)));
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  await page.goto(base + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(login);
  await page.keyboard.press('Tab');
  await page.keyboard.type('Demo-Pass-2026');
  await page.keyboard.press('Enter');

  await page.locator('main h1').waitFor();
  return { ctx, page, errors };
}
const shot = async (page, name) => page.screenshot({ path: `${ev}/${name}.jpg`, type: 'jpeg', quality: 60 });

// 1. English admin: G then N, list, open purchase order, yearly reset with preview, save by Enter.
{
  const { ctx, page, errors } = await signIn('admin@alnoor.example');
  await page.locator('body').click({ position: { x: 5, y: 5 } }).catch(() => {});
  await page.keyboard.press('Escape');
  await page.keyboard.press('g'); await page.keyboard.press('n');
  await page.waitForURL('**/numbering/sequences');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  note(`EN: G N reached ${new URL(page.url()).pathname}; h1="${await page.locator('main h1').innerText()}"; rows shown ${await page.locator('table[role=grid] tbody tr').count()}`);
  await shot(page, '01-en-list');
  // open the first row by keyboard: focus grid, Enter
  const row = page.locator('table[role=grid] tbody tr', { has: page.locator('td', { hasText: /^Purchase order$/ }) }).first();
  await row.click();
  await page.locator('.list-record input[name="prefix"]:focus').waitFor();
  const sel = await page.evaluate(() => { const i = document.activeElement; return [i.value, i.selectionStart, i.selectionEnd]; });
  note(`EN: purchase order panel opened, prefix focused with "${sel[0]}" selected ${sel[1]}..${sel[2]}`);
  const before = await page.locator('[data-testid="next-number"]').innerText();
  await page.keyboard.type('PO/{YY}/');
  await page.keyboard.press('Tab'); await page.keyboard.type('4');
  // to reset select
  await page.locator('.list-record select[name="reset"]').focus();
  await page.locator('.list-record select[name="reset"]').selectOption('yearly');
  const preview = await page.locator('.nb-preview').innerText();
  note(`EN: preview before "${before}", after typing: ${preview.replace(/\s+/g, ' ')}`);
  await shot(page, '02-en-yearly-preview');
  await page.locator('.list-record input[name="prefix"]').focus();
  await page.keyboard.press('Enter');
  await page.locator('.list-record [role="status"]').waitFor();
  note(`EN: saved by Enter, status "${(await page.locator('.list-record [role="status"]').innerText()).trim()}"`);
  // invalid: yearly without year token
  await page.locator('.list-record input[name="prefix"]').fill('PO-');
  await page.waitForTimeout(300);
  const err = await page.locator('.list-record').innerText();
  const m = err.match(/[^\n]*(year|Year)[^\n]*/g) || [];
  note(`EN: prefix "PO-" with yearly reset -> messages: ${JSON.stringify(m.slice(0, 3))}`);
  await shot(page, '03-en-invalid-yearly');
  await page.keyboard.press('Escape'); await page.waitForTimeout(300);
  const dlg = await page.locator('[role=dialog], [role=alertdialog]').count();
  note(`EN: Escape with unsaved change -> dialogs ${dlg}`);
  if (dlg) { await shot(page, '04-en-leave-guard'); await page.keyboard.press('Escape'); }
  note(`EN: page errors ${JSON.stringify(errors.slice(0, 3))}`);
  await ctx.close();
}
// 2. Arabic admin
{
  const { ctx, page, errors } = await signIn('admin.ar@alnoor.example');
  await page.keyboard.press('Escape');
  await page.keyboard.press('g'); await page.keyboard.press('n');
  await page.waitForURL('**/numbering/sequences');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  const dir = await page.evaluate(() => document.documentElement.dir + ' ' + document.documentElement.lang);
  const h1 = await page.locator('main h1').innerText();
  const heads = await page.locator('table[role=grid] thead th').allInnerTexts();
  note(`AR: dir/lang ${dir}; h1 "${h1}"; headers ${JSON.stringify(heads)}`);
  await shot(page, '05-ar-list');
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.locator('.list-record input[name="prefix"]').waitFor();
  await page.waitForTimeout(500);
  const panel = await page.locator('.list-record').innerText();
  const latin = (panel.match(/[A-Za-z]{4,}/g) || []).filter(w => !/^(YYYY|BRANCH)$/.test(w));
  note(`AR: panel Latin words (outside numbers/tokens): ${JSON.stringify([...new Set(latin)].slice(0, 15))}`);
  await page.locator('.list-record select[name="reset"]').selectOption('monthly');
  await page.locator('.list-record input[name="prefix"]').fill('INV-{YYYY}');
  await page.waitForTimeout(300);
  note(`AR: monthly without {MM} -> ${(await page.locator('.list-record').innerText()).split('\n').filter(l => /شهر/.test(l)).slice(0, 3).join(' | ')}`);
  await shot(page, '06-ar-panel-validation');
  note(`AR: page errors ${JSON.stringify(errors.slice(0, 3))}`);
  await ctx.close();
}
// 3. Viewer (read-only) and no-roles user
{
  const { ctx, page } = await signIn('viewer@alnoor.example');
  await page.goto(base + '/numbering/sequences');
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.locator('.list-record input[name="prefix"]').waitFor();
  note(`viewer: prefix disabled ${await page.locator('.list-record input[name="prefix"]').isDisabled()}; save buttons ${await page.locator('.list-record button', { hasText: /save/i }).count()}`);
  await shot(page, '07-viewer-readonly');
  await ctx.close();
}
{
  const { ctx, page } = await signIn('noaccess@alnoor.example');
  note(`no roles: menu link to numbering ${await page.locator('nav a[href="/numbering/sequences"]').count()}`);
  await page.goto(base + '/numbering/sequences');
  await page.waitForTimeout(1500);
  note(`no roles: direct URL shows "${(await page.locator('main').innerText()).slice(0, 120).replace(/\s+/g, ' ')}"; grid rows ${await page.locator('table[role=grid] tbody tr').count()}`);
  await shot(page, '08-noaccess-direct-url');
  await ctx.close();
}
await browser.close();
fs.writeFileSync(`${ev}/walk-notes.txt`, notes.join('\n') + '\n');
