import { launch, signIn } from './pw.mjs';
const base = process.argv[2]; const out = process.argv[3];
const browser = await launch();
const lines = []; const log = (...a) => { lines.push(a.join(' ')); console.log(...a); };
const count = p => p.locator('.list-count').first().textContent();
{
  const { page, context } = await signIn(browser, base, 'admin@alnoor.example');
  let t = Date.now();
  await page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first().click();
  await page.locator('table tbody tr').first().waitFor();
  log('EN users list rendered ms', Date.now() - t, 'count', await count(page), 'DOM rows', await page.locator('table tbody tr').count());
  log('search focused on arrival', await page.evaluate(() => document.activeElement?.getAttribute('type') || document.activeElement?.tagName));
  await page.screenshot({ path: out + '/01-users-list-100k-en.jpg', type: 'jpeg', quality: 70 });
  // quick search as you type
  t = Date.now();
  await page.keyboard.type('khalid haddad');
  await page.waitForFunction(() => /\d/.test(document.querySelector('.list-count')?.textContent || '') && !/100,005/.test(document.querySelector('.list-count')?.textContent));
  log('typed "khalid haddad" -> count', await count(page), 'ms', Date.now() - t);
  log('first row', (await page.locator('table tbody tr').first().innerText()).replace(/\s+/g, ' ').slice(0, 120));
  await page.screenshot({ path: out + '/02-search-as-you-type.jpg', type: 'jpeg', quality: 70 });
  // Enter opens the best match; Esc back
  await page.keyboard.press('Enter');
  await page.waitForTimeout(600);
  log('after Enter: record region', await page.getByRole('region', { name: 'Details' }).count());
  await page.keyboard.press('Escape');
  await page.waitForTimeout(300);
  // clear search, keyboard navigation in the grid
  await page.locator('input[type="search"]').first().fill('');
  await page.waitForTimeout(800);
  await page.locator('input[type="search"]').first().focus();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('ArrowDown');
  log('focused after ArrowDown x2', await page.evaluate(() => { const r = document.querySelector('tr.list-row.is-active'); return r ? (r.getAttribute('aria-rowindex') + ' ' + r.innerText.replace(/\s+/g,' ').slice(0,60)) : 'none'; }));
  t = Date.now();
  await page.keyboard.press('End');
  await page.waitForTimeout(1500);
  log('End -> focused', await page.evaluate(() => { const r = document.querySelector('tr.list-row.is-active'); return r ? (r.getAttribute('aria-rowindex') + ' ' + r.innerText.replace(/\s+/g,' ').slice(0,60)) : 'none'; }), 'ms', Date.now() - t, 'DOM rows', await page.locator('table tbody tr').count());
  await page.keyboard.press('Home');
  await page.waitForTimeout(800);
  log('Home -> focused', await page.evaluate(() => { const r = document.querySelector('tr.list-row.is-active'); return r ? (r.getAttribute('aria-rowindex') + ' ' + r.innerText.replace(/\s+/g,' ').slice(0,60)) : 'none'; }));
  await page.keyboard.press('Space');
  await page.keyboard.press('Shift+ArrowDown');
  await page.keyboard.press('Shift+ArrowDown');
  await page.waitForTimeout(300);
  log('selection bar', (await page.locator('.list-selectionbar').allInnerTexts()).join(' | ').replace(/\s+/g, ' ').slice(0, 200));
  await page.screenshot({ path: out + '/03-keyboard-selection-bulk.jpg', type: 'jpeg', quality: 70 });
  await page.keyboard.press('Escape');
  // header sort by keyboard: tab to Name header button and press Enter
  const nameHeader = page.getByRole('columnheader', { name: /Name/ }).first();
  const btns = await nameHeader.locator('button').allTextContents();
  log('name header buttons', JSON.stringify(btns));
  await nameHeader.locator('button').first().focus();
  await page.keyboard.press('Enter');
  await page.waitForTimeout(800);
  log('after Enter on Name header: aria-sort', await nameHeader.getAttribute('aria-sort'), 'first', (await page.locator('table tbody tr').first().innerText()).replace(/\s+/g, ' ').slice(0, 60));
  // column menu filter
  const menuBtn = nameHeader.locator('button[aria-haspopup]');
  if (await menuBtn.count()) {
    await menuBtn.first().click();
    log('column menu items', JSON.stringify(await page.getByRole('menuitem').allTextContents()));
    await page.keyboard.press('Escape');
  }
  const lang = page.getByRole('columnheader', { name: /Language/ }).locator('button[aria-haspopup]');
  if (await lang.count()) {
    await lang.first().click();
    await page.getByRole('menuitem', { name: /Group by/ }).click();
    await page.waitForTimeout(1200);
    log('grouped by language; group rows', (await page.locator('tr.list-group, [role="row"].list-group, .list-group-row').allInnerTexts()).map(s => s.replace(/\s+/g, ' ')).join(' | ').slice(0, 200));
    await page.screenshot({ path: out + '/04-group-by-language.jpg', type: 'jpeg', quality: 70 });
  }
  // columns chooser
  await page.getByRole('button', { name: 'Columns' }).click();
  log('column chooser', (await page.getByRole('dialog').first().innerText()).replace(/\s+/g, ' ').slice(0, 200));
  await page.keyboard.press('Escape');
  await context.close();
}
{
  const { page, context } = await signIn(browser, base, 'admin.ar@alnoor.example', { lang: 'ar' });
  await page.locator('nav a[href="/identity/users"]').first().click();
  await page.locator('table tbody tr').first().waitFor();
  log('AR dir', await page.locator('html').getAttribute('dir'), 'count', await count(page));
  log('AR headers', JSON.stringify(await page.getByRole('columnheader').allInnerTexts()));
  await page.keyboard.type('ماجد');
  await page.waitForTimeout(3000);
  await page.screenshot({ path: out + '/05-users-list-ar.jpg', type: 'jpeg', quality: 70 });
  log('AR search ماجد count', await count(page), 'rows', await page.locator('table tbody tr').count());
  await context.close();
}
await browser.close();
