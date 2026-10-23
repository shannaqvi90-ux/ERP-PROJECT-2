import { launch, signIn, api } from './pw.mjs';
const base = process.argv[2]; const out = process.argv[3];
const browser = await launch();
const log = (...a) => console.log(...a);
const count = p => p.locator('.list-count').first().textContent();
const name = 'Arabic speakers R14 ' + Date.now().toString(36);
{
  const { page, context } = await signIn(browser, base, 'admin@alnoor.example');
  await page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first().click();
  await page.locator('table tbody tr').first().waitFor();
  // Header filter by keyboard: focus the Language column menu button, Enter, choose Filter…
  const lang = page.getByRole('columnheader', { name: /Language/ }).locator('button[aria-haspopup]').first();
  await lang.focus(); await page.keyboard.press('Enter');
  await page.getByRole('menuitem', { name: 'Filter…' }).click();
  await page.locator('.list-filter').getByLabel('Arabic').check();
  await page.locator('.list-filter').getByRole('button', { name: 'Apply' }).click();
  await page.waitForTimeout(1200);
  log('header filter Language is one of Arabic ->', await count(page));
  // sort by status desc from header menu
  const st = page.getByRole('columnheader', { name: /^Name/ }).locator('button[aria-haspopup]').first();
  await st.click(); await page.getByRole('menuitem', { name: 'Sort descending' }).click();
  await page.waitForTimeout(800);
  log('sorted status desc; aria-sort', await page.getByRole('columnheader', { name: /^Name/ }).getAttribute('aria-sort'));
  // Save as shared default view
  await page.getByRole('button', { name: /^Views|^View:/ }).first().click();
  await page.getByRole('menuitem', { name: 'Save as a new view…' }).click();
  const dlg = page.locator('.list-save');
  await dlg.getByLabel('Name').fill(name);
  log('admin save dialog has share box', await dlg.getByText('Share with everyone who can see this list').count());
  await dlg.getByLabel('Share with everyone who can see this list').check();
  await dlg.getByLabel('Open this view when I open the list').check();
  await page.screenshot({ path: out + '/02-save-shared-view.jpg', type: 'jpeg', quality: 70 });
  await dlg.getByRole('button', { name: 'Save' }).click();
  await page.waitForTimeout(1000);
  log('after save, views button', (await page.getByRole('button', { name: /^View:/ }).first().textContent())?.trim(), 'url', page.url().replace(base, ''));
  // open record by keyboard and check "columns beside a record"
  await page.locator('input[type="search"]').first().focus();
  await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
  await page.waitForTimeout(1000);
  log('record region open', await page.getByRole('region', { name: 'Details' }).count(), 'waiting-columns button', await page.locator('.list-more-button').count(),
    (await page.locator('.list-more-button').first().getAttribute('aria-label').catch(() => null)));
  log('visible headers with record open', JSON.stringify((await page.getByRole('columnheader').allInnerTexts()).map(s => s.replace(/\s+/g, ' ').trim())));
  await page.screenshot({ path: out + '/03-record-open-beside-list.jpg', type: 'jpeg', quality: 70 });
  await page.keyboard.press('Alt+w'); await page.waitForTimeout(600);
  log('after Alt+W headers', JSON.stringify((await page.getByRole('columnheader').allInnerTexts()).map(s => s.replace(/\s+/g, ' ').trim())));
  await context.close();
}
{
  const { page, context } = await signIn(browser, base, 'viewer@alnoor.example');
  await page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first().click();
  await page.locator('table tbody tr').first().waitFor();
  await page.waitForTimeout(800);
  log('viewer opens Users on', (await page.getByRole('button', { name: /^Views|^View:/ }).first().textContent())?.trim(), 'count', await count(page));
  await page.getByRole('button', { name: /^Views|^View:/ }).first().click();
  log('viewer views menu', JSON.stringify(await page.locator('.list-views').getByRole('menuitem').allInnerTexts()));
  // change the view (sort) and check menu again
  await page.keyboard.press('Escape');
  const nm = page.getByRole('columnheader', { name: /Name/ }).locator('button').first();
  await nm.click(); await page.waitForTimeout(800);
  await page.getByRole('button', { name: /^Views|^View:/ }).first().click();
  log('viewer views menu after change', JSON.stringify(await page.locator('.list-views').getByRole('menuitem').allInnerTexts()));
  await page.locator('.list-views').getByRole('menuitem', { name: 'Save as a new view…' }).click();
  log('viewer save dialog share box', await page.locator('.list-save').getByText('Share with everyone who can see this list').count());
  await page.screenshot({ path: out + '/04-viewer-save-no-share.jpg', type: 'jpeg', quality: 70 });
  log('viewer bulk actions after select', await (async () => { await page.keyboard.press('Escape'); await page.locator('input[type="search"]').first().focus(); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Space'); await page.waitForTimeout(300); return (await page.locator('.list-selectionbar').allInnerTexts()).join('|').replace(/\s+/g, ' '); })());
  await context.close();
}
// clean the shared view
const A = await api(base, 'admin@alnoor.example');
const v = (await A.get('/api/lists/identity.users/views')).body.items.find(x => x.name === name);
log('cleanup delete', v ? (await A.del('/api/lists/identity.users/shared-views/' + v.id)).status : 'not found');
await browser.close();
