// Screens of the planted stack: L13 (gulfsteel sees alnoor's count for "ma") and P10 (a reader is offered
// to change and delete the workspace's shared view), plus what the server says to the reader's delete.
import { launch, signIn, api } from './pw.mjs';
const base = process.argv[2]; const out = process.argv[3];
const browser = await launch();
// L13: alnoor asks first through the API, then gulfsteel types in the Users screen.
const a = await api(base, 'admin@alnoor.example');
console.log('alnoor ma total', (await a.get('/api/identity/users?take=1&search=ma')).body.total);
{
  const { page, context } = await signIn(browser, base, 'admin@gulfsteel.example');
  await page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first().click();
  await page.locator('input[type="search"]').first().waitFor();
  await page.keyboard.type('ma');
  await page.waitForTimeout(1500);
  console.log('gulfsteel screen count:', await page.locator('.list-count').first().textContent());
  await page.screenshot({ path: out + '/plant-L13-second-tenant-sees-first-tenants-search-count.jpg', type: 'jpeg', quality: 70 });
  await context.close();
}
// P10: a shared view exists; the reader picks it.
const name = 'Team view ' + Date.now();
const def = (await a.get('/api/lists/identity.users/definition')).body;
const created = await a.post('/api/lists/identity.users/shared-views', { name, columns: ['displayName', 'email'] });
console.log('shared view created', created.status);
{
  const { page, context } = await signIn(browser, base, 'viewer@alnoor.example');
  await page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first().click();
  await page.locator('input[type="search"]').first().waitFor();
  await page.getByRole('button', { name: /^View:/ }).click();
  await page.getByRole('menuitemradio', { name }).click();
  await page.getByRole('button', { name: /^View:/ }).click();
  const items = await page.getByRole('menuitem').allTextContents();
  console.log('reader menu items:', JSON.stringify(items));
  await page.screenshot({ path: out + '/plant-P10-reader-offered-delete-of-shared-view.jpg', type: 'jpeg', quality: 70 });
  const del = page.getByRole('menuitem', { name: 'Delete this view' });
  if (await del.count()) {
    const responses = [];
    page.on('response', r => { if (r.url().includes('/views')) responses.push(`${r.request().method()} ${new URL(r.url()).pathname} ${r.status()}`); });
    await del.click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Delete this view' }).click();
    await page.waitForTimeout(1500);
    console.log('reader delete answered:', JSON.stringify(responses));
    console.log('alert text:', await page.locator('[role="alert"]').allTextContents());
  }
  await context.close();
}
await browser.close();
